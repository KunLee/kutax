using System.Security.Claims;
using System.Text.Json;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Identity;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Processing;
using KuTax.Infrastructure.Rules;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Api.Features.Claims;

/// <summary>Administrator endpoints for yearly rule updates: check, AI draft, import, compare, activate or reject.</summary>
public static class RulePackEndpoints
{
    public static void MapRulePacks(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/rule-packs").RequireAuthorization(p=>p.RequireRole(ApplicationRoles.Administrator));
        group.MapGet("",async(KuTaxDbContext db,RulePackService service,IAiOptionsSource ai,CancellationToken ct)=>
        {
            var rules=await db.LoadRulesAsync(ct);var next=await service.NextYearAsync(ct);
            var check=await db.RuleSourceChecks.AsNoTracking().SingleOrDefaultAsync(c=>c.FinancialYearStart==next,ct);
            var packs=(await db.RulePacks.AsNoTracking().Select(p=>new{p.Id,p.FinancialYearStart,p.Status,p.Origin,p.CreatedAt,p.CreatedBy,p.DecidedAt,p.ErrorCode,p.ReportJson}).ToListAsync(ct))
                .OrderByDescending(p=>p.CreatedAt).Take(50).ToList();
            var reviewed=(await db.TaxRuleReviews.AsNoTracking().Select(r=>r.RuleVersion).ToListAsync(ct)).ToHashSet();
            return Results.Ok(new{
                years=rules.Active.GroupBy(r=>r.FinancialYearStart).OrderByDescending(g=>g.Key).Select(g=>new{year=g.Key,rules=g.Count(),reviewed=g.Count(r=>reviewed.Contains(r.Version)),
                    revision=g.Max(r=>TaxRuleSet.Revision(r.Version)),fromPack=g.Any(r=>r.PackId!=null)}),
                nextYear=next,check,aiConfigured=AiPrivacy.Configured(await ai.GetAsync(ct)),
                packs=packs.Select(p=>{var report=JsonSerializer.Deserialize<PackReport>(p.ReportJson,RulePackDocument.Json);
                    return new{p.Id,p.FinancialYearStart,p.Status,p.Origin,p.CreatedAt,p.DecidedAt,p.ErrorCode,errors=report?.Errors?.Length??0,warnings=report?.Warnings?.Length??0,changed=report?.ChangedFields??0};})});
        });
        group.MapPost("/check",async(ClaimsPrincipal principal,RulePackService service,CancellationToken ct)=>
            Results.Ok(await service.CheckAsync(await service.NextYearAsync(ct),Owner(principal),ct)));
        // Drafts (or re-drafts, for comparison) any year from the official instructions, once they exist.
        group.MapPost("/draft",async(DraftRequest request,ClaimsPrincipal principal,KuTaxDbContext db,IAiOptionsSource ai,CancellationToken ct)=>
        {
            if(request.FinancialYearStart is <2024 or >2100)return Results.BadRequest();
            if(!AiPrivacy.Configured(await ai.GetAsync(ct)))return Results.Problem("Set up AI in Settings first, or import a pack.",statusCode:409);
            if(await db.RulePacks.AnyAsync(p=>p.FinancialYearStart==request.FinancialYearStart&&(p.Status=="Queued"||p.Status=="Drafting"),ct))return Results.Problem("A draft for that year is already being prepared.",statusCode:409);
            var pack=new RulePack{FinancialYearStart=request.FinancialYearStart,CreatedBy=Owner(principal)};
            db.RulePacks.Add(pack);await db.SaveChangesAsync(ct);
            return Results.Accepted($"/api/rule-packs/{pack.Id}",new{pack.Id});
        });
        group.MapGet("/{id:guid}",async(Guid id,KuTaxDbContext db,CancellationToken ct)=>
        {
            var pack=await db.RulePacks.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id,ct);
            if(pack is null)return Results.NotFound();
            return Results.Ok(new{pack.Id,pack.FinancialYearStart,pack.Status,pack.Origin,pack.CreatedAt,pack.CreatedBy,pack.DecidedAt,pack.DecidedBy,pack.Note,pack.ErrorCode,retryAt=pack.Status=="Queued"?pack.LeaseUntil:null,
                pack=pack.Status is "Queued" or "Drafting" or "Failed"?null:JsonSerializer.Deserialize<RulePackDocument>(pack.PackJson,RulePackDocument.Json),
                report=pack.Status is "Queued" or "Drafting" or "Failed"?null:JsonSerializer.Deserialize<PackReport>(pack.ReportJson,RulePackDocument.Json)});
        });
        group.MapPost("/{id:guid}/activate",async(Guid id,PackDecision request,ClaimsPrincipal principal,RulePackService service,CancellationToken ct)=>
        {
            if(!request.Confirm||string.IsNullOrWhiteSpace(request.Note)||request.Note.Length>2000)return Results.BadRequest();
            var problem=await service.ActivateAsync(id,Owner(principal),request.Note.Trim(),ct);
            return problem is null?Results.NoContent():problem=="Not found."?Results.NotFound():Results.Problem(problem,statusCode:409);
        });
        group.MapPost("/{id:guid}/reject",async(Guid id,PackDecision request,ClaimsPrincipal principal,KuTaxDbContext db,CancellationToken ct)=>
        {
            var pack=await db.RulePacks.SingleOrDefaultAsync(p=>p.Id==id,ct);
            if(pack is null)return Results.NotFound();
            if(pack.Status is not("Draft" or "Failed"))return Results.Problem("Only a draft can be rejected.",statusCode:409);
            var owner=Owner(principal);pack.Status="Rejected";pack.DecidedBy=owner;pack.DecidedAt=DateTimeOffset.UtcNow;pack.Note=request.Note?.Trim();
            db.AuditEvents.Add(new(){OwnerId=owner,ActorId=owner,ResourceId=id,Action="RulePackRejected",DetailsJson=JsonSerializer.Serialize(new{request.Note},ReceiptCandidate.Json)});
            await db.SaveChangesAsync(ct);return Results.NoContent();
        });
        group.MapPost("/import",async(HttpRequest http,ClaimsPrincipal principal,RulePackService service,CancellationToken ct)=>
        {
            if(http.ContentLength>512*1024)return Results.Problem("A rule pack must be under 512 KB.",statusCode:413);
            RulePackDocument? document;
            try{document=await JsonSerializer.DeserializeAsync<RulePackDocument>(http.Body,RulePackDocument.Json,ct);}
            catch(JsonException error){return Results.Problem("That isn't a valid rule pack: "+error.Message,statusCode:400);}
            if(document?.Rules is null||document.Sources is null)return Results.Problem("That isn't a valid rule pack.",statusCode:400);
            var pack=await service.ImportAsync(document,Owner(principal),ct);
            return Results.Created($"/api/rule-packs/{pack.Id}",new{pack.Id});
        });
        // The rules in force for a year, as an editable pack (the starting point for an import).
        group.MapGet("/export/{year:int}",async(int year,KuTaxDbContext db,CancellationToken ct)=>
        {
            var rules=await db.LoadRulesAsync(ct);
            if(!rules.ForYear(year).Any())return Results.NotFound();
            var json=JsonSerializer.SerializeToUtf8Bytes(TaxRuleCatalog.ToPack(year,rules.Active),RulePackDocument.Json);
            return Results.File(json,"application/json",$"kutax-rules-FY{year}-{(year+1)%100:00}.json");
        });
    }
    private static string Owner(ClaimsPrincipal principal)=>principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
public sealed record DraftRequest(int FinancialYearStart);
public sealed record PackDecision(bool Confirm,string? Note);
