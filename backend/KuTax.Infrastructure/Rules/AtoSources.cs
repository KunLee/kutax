using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KuTax.Core.Claims;
using UglyToad.PdfPig;

namespace KuTax.Infrastructure.Rules;

/// <summary>The official individual tax return instructions for a year, as page text, with the download's checksum.</summary>
public sealed record InstructionsDocument(int FinancialYearStart,string Url,string Sha256,string[] Pages);

public interface IAtoSources
{
    /// <summary>Whether the instructions for a financial year have been published (the ATO's ordering site serves a PDF).</summary>
    Task<bool> InstructionsPublishedAsync(int financialYearStart,CancellationToken ct);
    Task<InstructionsDocument> InstructionsAsync(int financialYearStart,CancellationToken ct);
    /// <summary>The readable text of an ATO guidance page, or null if it couldn't be fetched.</summary>
    Task<string?> GuidanceTextAsync(string url,CancellationToken ct);
}

/// <summary>Fetches public ATO publications only; nothing about any person is sent. Redirects and other hosts are refused.</summary>
public sealed partial class AtoSources(IHttpClientFactory clients) : IAtoSources
{
    public const string Client="ato";
    private const long MaxPdfBytes=60L*1024*1024, MaxPageBytes=4L*1024*1024;
    private const int MaxGuidanceChars=24_000;

    public async Task<bool> InstructionsPublishedAsync(int year,CancellationToken ct)
    {
        using var response=await clients.CreateClient(Client).GetAsync(TaxRuleCatalog.Instructions(year),HttpCompletionOption.ResponseHeadersRead,ct);
        return response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentType?.MediaType=="application/pdf"&&(response.Content.Headers.ContentLength??0)>1_000_000;
    }

    public async Task<InstructionsDocument> InstructionsAsync(int year,CancellationToken ct)
    {
        var url=TaxRuleCatalog.Instructions(year);
        var bytes=await DownloadAsync(url,MaxPdfBytes,"application/pdf",ct);
        using var pdf=PdfDocument.Open(bytes);
        return new(year,url,Convert.ToHexString(SHA256.HashData(bytes)),[..pdf.GetPages().Select(page=>page.Text)]);
    }

    public async Task<string?> GuidanceTextAsync(string url,CancellationToken ct)
    {
        if(!RuleEngine.OfficialUrl(url))return null;
        try
        {
            var html=System.Text.Encoding.UTF8.GetString(await DownloadAsync(url,MaxPageBytes,"text/html",ct));
            var main=html.IndexOf("<main",StringComparison.OrdinalIgnoreCase) is var start and >=0?html[start..]:html;
            if(main.IndexOf("</main>",StringComparison.OrdinalIgnoreCase) is var end and >0)main=main[..end];
            var text=WebUtility.HtmlDecode(Tags().Replace(Scripts().Replace(main," ")," "));
            text=Spaces().Replace(text," ").Trim();
            return text.Length>MaxGuidanceChars?text[..MaxGuidanceChars]:text;
        }
        catch(Exception error) when(error is HttpRequestException or InvalidDataException or TaskCanceledException){return null;}
    }

    private async Task<byte[]> DownloadAsync(string url,long limit,string mediaType,CancellationToken ct)
    {
        using var response=await clients.CreateClient(Client).GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentType?.MediaType!=mediaType)throw new InvalidDataException($"The publication isn't available ({(int)response.StatusCode}).");
        if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("The publication is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();
        var chunk=new byte[81920];int count;
        while((count=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+count>limit)throw new InvalidDataException("The publication is too large.");buffer.Write(chunk,0,count);}
        return buffer.ToArray();
    }

    [GeneratedRegex(@"(?is)<(script|style|noscript|svg)\b.*?</\1>")] private static partial Regex Scripts();
    [GeneratedRegex(@"(?s)<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
