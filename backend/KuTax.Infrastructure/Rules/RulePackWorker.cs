using System.Threading.Channels;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KuTax.Infrastructure.Rules;

/// <summary>
/// Once a financial year: from June of the year a return covers, checks daily for that year's ATO instructions until they
/// appear, then drafts the year's rule pack with AI for an administrator to review. Also runs drafts queued by hand.
/// </summary>
/// <summary>
/// Years someone is working on that may need checking for new ATO instructions. Pages that show a year's figures add
/// the year here (cheaply, without waiting); the worker picks it up. Nothing is checked on a timer.
/// </summary>
public sealed class RuleCheckRequests
{
    private readonly Channel<int> years=Channel.CreateBounded<int>(new BoundedChannelOptions(16){FullMode=BoundedChannelFullMode.DropOldest});
    public void Request(int financialYearStart)=>years.Writer.TryWrite(financialYearStart);
    internal ChannelReader<int> Reader=>years.Reader;
}

/// <summary>
/// Just in time: drafts rules only when a year being worked on needs them (or when drafts were left queued, for
/// example paused for the AI provider's quota), then sleeps until the next request or the paused draft's retry time.
/// </summary>
public sealed class RulePackWorker(IServiceScopeFactory scopes,ILogger<RulePackWorker> logger,RuleCheckRequests? requests=null) : BackgroundService
{
    private static readonly TimeSpan Tick=TimeSpan.FromMinutes(5), CheckEvery=TimeSpan.FromHours(20), Lease=TimeSpan.FromMinutes(45);
    internal const int MaxAttempts=3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader=(requests??new RuleCheckRequests()).Reader;
        while(!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? wake=null;
            try
            {
                while(reader.TryRead(out var year))await CheckYearAsync(year,stoppingToken);
                while(await DraftOneAsync(stoppingToken)){}
                wake=await NextDraftDueAsync(stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception error){logger.LogWarning("Rule pack worker failed ({ExceptionType}); retrying later.",error.GetType().Name);wake=Tick;}
            // Sleep until someone works on a year, or a paused draft may resume; never on a fixed schedule.
            var request=reader.WaitToReadAsync(stoppingToken).AsTask();
            if(wake is { } due)await Task.WhenAny(request,Task.Delay(due,stoppingToken));else await request;
        }
    }

    private sealed record DraftProgress(Dictionary<string,string> Progress);
    private static readonly JsonSerializerOptions Web=new(JsonSerializerDefaults.Web);

    /// <summary>The instructions for financial year Y (published as the Y+1 edition) usually appear in June of Y+1.</summary>
    public static bool InCheckWindow(int year,DateOnly today)=>today>=new DateOnly(year+1,6,1);

    /// <summary>When the earliest queued draft can be picked up again, or null when nothing is waiting.</summary>
    private async Task<TimeSpan?> NextDraftDueAsync(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        var leases=await db.RulePacks.AsNoTracking().Where(p=>p.Status=="Queued"||p.Status=="Drafting").Select(p=>p.LeaseUntil).ToListAsync(ct);
        if(leases.Count==0)return null;
        var due=leases.Min(l=>l??DateTimeOffset.UtcNow)-DateTimeOffset.UtcNow;
        return due<TimeSpan.FromSeconds(5)?TimeSpan.FromSeconds(5):due;
    }

    /// <summary>
    /// Checks for a year's ATO instructions when someone works on that year: only a year newer than any rules in force,
    /// only once its instructions are normally out (June after it ends), and at most once a day.
    /// </summary>
    public async Task CheckYearAsync(int year,CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var service=scope.ServiceProvider.GetRequiredService<RulePackService>();var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        if(year<await service.NextYearAsync(ct))return;
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Perth").DateTime);
        if(!InCheckWindow(year,today))return;
        var last=await db.RuleSourceChecks.AsNoTracking().SingleOrDefaultAsync(c=>c.FinancialYearStart==year,ct);
        if(last is not null&&(last.PublishedAt is not null&&await db.RulePacks.AnyAsync(p=>p.FinancialYearStart==year,ct)||DateTimeOffset.UtcNow-last.CheckedAt<CheckEvery))return;
        var check=await service.CheckAsync(year,"system",ct);
        logger.LogInformation("Checked for FY {Year} instructions: {Status}.",year,check.PublishedAt is null?"not yet published":"published");
    }

    /// <summary>Drafts the oldest queued pack. Returns false when there was nothing to do.</summary>
    public async Task<bool> DraftOneAsync(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<KuTaxDbContext>();
        var now=DateTimeOffset.UtcNow;
        var pack=(await db.RulePacks.Where(p=>p.Status=="Queued"||p.Status=="Drafting").ToListAsync(ct))
            .Where(p=>p.Status=="Queued"&&(p.LeaseUntil is null||p.LeaseUntil<now)||p.Status=="Drafting"&&p.LeaseUntil<now).OrderBy(p=>p.CreatedAt).FirstOrDefault();
        if(pack is null)return false;
        pack.Status="Drafting";pack.LeaseUntil=now+Lease;pack.Attempts++;
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return true;}
        // Answers from an interrupted attempt are kept in the pack while it is queued, so quota waits don't repeat work.
        var progress=pack.PackJson.Contains("\"progress\"",StringComparison.Ordinal)?JsonSerializer.Deserialize<DraftProgress>(pack.PackJson,Web)?.Progress??[]:new Dictionary<string,string>();
        try
        {
            var rules=await db.LoadRulesAsync(ct);
            var template=RulePackService.Template(pack.FinancialYearStart,rules)??throw new InvalidOperationException("There are no earlier rules to start from.");
            var (document,report)=await scope.ServiceProvider.GetRequiredService<IRulePackDrafter>().DraftAsync(pack.FinancialYearStart,template,RulePackService.Baseline(pack.FinancialYearStart,rules)!,progress,ct);
            pack.PackJson=JsonSerializer.Serialize(document,RulePackDocument.Json);pack.ReportJson=JsonSerializer.Serialize(report,RulePackDocument.Json);
            pack.Status="Draft";pack.LeaseUntil=null;pack.ErrorCode=null;
            logger.LogInformation("Drafted FY {Year} rule pack: {Errors} errors, {Warnings} warnings.",pack.FinancialYearStart,report.Errors.Length,report.Warnings.Length);
        }
        catch(Processing.ProviderRateLimitedException limited)
        {
            // A quota, not a fault: wait as long as the provider asks (daily quotas can be hours) without using up an attempt.
            pack.PackJson=JsonSerializer.Serialize(new DraftProgress(progress),Web);
            pack.Attempts--;pack.Status="Queued";pack.ErrorCode="RateLimited";pack.LeaseUntil=DateTimeOffset.UtcNow+(limited.RetryAfter<TimeSpan.FromMinutes(1)?TimeSpan.FromMinutes(1):limited.RetryAfter);
            logger.LogInformation("Rule pack draft for FY {Year} waits for the AI quota until {Until}.",pack.FinancialYearStart,pack.LeaseUntil);
        }
        catch(Exception error) when(error is not OperationCanceledException||!ct.IsCancellationRequested)
        {
            pack.ErrorCode=error switch{InvalidOperationException=>"NotConfigured",InvalidDataException=>"SourceUnavailable",HttpRequestException=>"Unavailable",JsonException=>"InvalidAnswer",_=>"Failed"};
            pack.Status=pack.Attempts>=MaxAttempts||pack.ErrorCode=="NotConfigured"?"Failed":"Queued";pack.LeaseUntil=DateTimeOffset.UtcNow.AddMinutes(15*pack.Attempts);
            logger.LogWarning("Rule pack draft for FY {Year} attempt {Attempt} failed ({ExceptionType}).",pack.FinancialYearStart,pack.Attempts,error.GetType().Name);
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
