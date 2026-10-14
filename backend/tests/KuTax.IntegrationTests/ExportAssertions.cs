using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Persistence;
using KuTax.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PdfSharp.Pdf.IO;

namespace KuTax.IntegrationTests;
internal static class ExportAssertions
{
    internal static async Task<Guid> CreateAndVerify(HttpClient client,HttpClient stranger,KuTaxDbContext db,IServiceProvider services,decimal approvedTotal)
    {
        var preview=await client.GetFromJsonAsync<JsonElement>("/api/tax-years/2025/export-preview");Assert.Empty(preview.GetProperty("blockers").EnumerateArray());
        var request=new{previewHash=preview.GetProperty("hash").GetString(),confirm=true,acknowledgeExceptions=true,note=(string?)null}; // the note is optional
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/tax-years/2025/exports",request with{previewHash="stale"})).StatusCode);
        var created=await client.PostAsJsonAsync("/api/tax-years/2025/exports",request);Assert.Equal(HttpStatusCode.Accepted,created.StatusCode);var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/tax-exports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.PutAsJsonAsync($"/api/tax-exports/{id}/entry-progress",new{status="Checked"})).StatusCode);
        await db.ExportJobs.Where(j=>j.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(j=>j.Status,"Processing").SetProperty(j=>j.LeaseUntil,DateTimeOffset.UtcNow.AddMinutes(-1)));
        var worker=new ExportWorker(services.GetRequiredService<IServiceScopeFactory>(),NullLogger<ExportWorker>.Instance);Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        var package=await client.GetFromJsonAsync<JsonElement>($"/api/tax-exports/{id}");Assert.Equal("Complete",package.GetProperty("status").GetString());
        var snapshot=await db.ExportSnapshots.AsNoTracking().SingleAsync(s=>s.Id==id);Assert.Equal(snapshot.Sha256,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.SnapshotJson))));
        Assert.Equal(approvedTotal,package.GetProperty("model").GetProperty("assessment").GetProperty("approvedTotal").GetDecimal());
        Assert.Equal("Approved in KuTax.",package.GetProperty("model").GetProperty("approvalNote").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await client.PutAsJsonAsync($"/api/tax-exports/{id}/entry-progress",new{status="Checked"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync($"/api/tax-exports/{id}/entry-progress",new{status="Lodged"})).StatusCode);
        var output=Path.GetFullPath(Path.Combine("artifacts","export-checks"));Directory.CreateDirectory(output);
        foreach(var file in package.GetProperty("files").EnumerateArray())
        {
            var kind=file.GetProperty("kind").GetString();Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/tax-exports/{id}/files/{kind}")).StatusCode);
            var bytes=await client.GetByteArrayAsync($"/api/tax-exports/{id}/files/{kind}");Assert.Equal(file.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(bytes)));
            File.WriteAllBytes(Path.Combine(output,file.GetProperty("fileName").GetString()!),bytes);
            if(kind is "workbook" or "workbook-zh")
            {
                using var stream=new MemoryStream(bytes);using var workbook=new XLWorkbook(stream);Assert.Equal(approvedTotal,workbook.Worksheets.First().Cell(2,2).GetValue<decimal>());
                var expenses=workbook.Worksheet(kind=="workbook"?"All expenses":"全部费用");var sum=expenses.RowsUsed().Skip(1).Where(r=>r.Cell(10).GetString()==(kind=="workbook"?"Approved":"已批准")).Sum(r=>r.Cell(9).GetValue<decimal>());Assert.Equal(approvedTotal,sum);
                Assert.All(workbook.Worksheets.SelectMany(s=>s.CellsUsed()),cell=>Assert.False(cell.HasFormula));
            }
            else if(kind=="evidence")
            {
                using var stream=new MemoryStream(bytes);using var zip=new ZipArchive(stream);using var manifestStream=zip.GetEntry("manifest.json")!.Open();using var manifest=JsonDocument.Parse(manifestStream);Assert.Equal(approvedTotal,manifest.RootElement.GetProperty("approvedTotal").GetDecimal());
                foreach(var document in manifest.RootElement.GetProperty("documents").EnumerateArray()){using var original=zip.GetEntry(document.GetProperty("path").GetString()!)!.Open();Assert.Equal(document.GetProperty("sha256").GetString()!.ToUpperInvariant(),Convert.ToHexString(SHA256.HashData(original)));}
            }
            else{using var stream=new MemoryStream(bytes);using var pdf=PdfReader.Open(stream,PdfDocumentOpenMode.Import);Assert.True(pdf.PageCount>=1);Assert.Contains(kind switch{"summary"=>"Annual tax summary","guide"=>"myTax entry guide","summary-zh"=>"年度报税摘要",_=>"myTax 填写指南"},pdf.Info.Title);}
        }
        Assert.Equal(7,package.GetProperty("files").GetArrayLength()); // summary, guide and spreadsheet in English and Chinese, plus the evidence archive
        // "Download all": the pack in one ZIP, in the chosen language — and only for its owner.
        foreach(var (lang,guide) in new[]{("zh","myTax填写指南"),("en","myTax_Entry_Guide")})
        {
            var all=await client.GetByteArrayAsync($"/api/tax-exports/{id}/download?lang={lang}");
            using var everything=new ZipArchive(new MemoryStream(all));
            Assert.Equal(4,everything.Entries.Count);
            Assert.Contains(everything.Entries,e=>e.FullName.StartsWith(guide)&&e.FullName.EndsWith(".pdf"));
            Assert.Contains(everything.Entries,e=>e.FullName.EndsWith(".zip"));
        }
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"/api/tax-exports/{id}/download?lang=zh")).StatusCode);
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ExportSnapshots\" SET \"Sha256\"='tampered' WHERE \"Id\"={id}"));
        return id;
    }
}

