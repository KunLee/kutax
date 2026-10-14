using System.Net;
using System.Text;
using System.Text.Json;
using KuTax.Core.Receipts;
using KuTax.Infrastructure.Processing;
using KuTax.Infrastructure.Reports;
using Microsoft.Extensions.Configuration;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace KuTax.Api.Tests;
public sealed class GroqExtractorTests
{
    private static readonly ReceiptCandidate Valid=new(new("Synthetic Union",.98m,"SYNTHETIC UNION"),new("2025-08-01",.95m,"Date: 1 August 2025"),new(110m,.99m,"Total paid AUD 110.00"),new(10m,.95m,"GST 10.00"),"AUD");
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    private sealed class FakeGroq(string content,string finishReason="stop",HttpStatusCode status=HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request,JsonDocument Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls.Add((request,JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct))));
            var body=JsonSerializer.Serialize(new{choices=new[]{new{finish_reason=finishReason,message=new{role="assistant",content}}}});
            var response=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};
            if(status==HttpStatusCode.TooManyRequests)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        }
    }
    private sealed class FixedSource(AiOptions options) : IAiOptionsSource { public Task<AiOptions> GetAsync(CancellationToken ct)=>Task.FromResult(options); }
    private static AiOptions Config(Dictionary<string,string?>? overrides=null)
    {
        var settings=new Dictionary<string,string?>{["Ai:Enabled"]="true",["Ai:Provider"]="Groq",["Ai:ApiKey"]="synthetic-groq-key",["Ai:Model"]="synthetic/vision",
            ["Ai:TextModel"]="synthetic/text",["Ai:Region"]="Synthetic region",["Ai:RetentionNotice"]="Synthetic retention notice"};
        foreach(var (key,value) in overrides??[])settings[key]=value;
        return AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }
    private static async Task<(ReceiptCandidate Candidate,string Provider,string Model)> Extract(FakeGroq groq,string contentType,byte[] bytes,bool consent=true,AiOptions? options=null)
    {
        var extractor=new ReceiptExtractor(new FixedSource(options??Config()),new ConfigurationBuilder().Build(),new HttpClient(groq));
        var document=new ReceiptDocument{OwnerId="synthetic",OriginalFileName="receipt",ContentType=contentType,Size=bytes.Length};
        return await extractor.ExtractAsync(document,new MemoryStream(bytes),consent,Ct);
    }
    private static byte[] TextPdf()
    {
        ReportFonts.Initialise();
        using var pdf=new PdfDocument();var page=pdf.AddPage();
        using(var graphics=XGraphics.FromPdfPage(page))
            foreach(var (line,i) in new[]{"SYNTHETIC UNION - TEST RECEIPT","Date: 1 August 2025","Total paid AUD 110.00","GST 10.00"}.Select((l,i)=>(l,i)))
                graphics.DrawString(line,new XFont("Noto Sans",12),XBrushes.Black,new XPoint(40,60+i*24));
        using var stream=new MemoryStream();pdf.Save(stream);return stream.ToArray();
    }

    [Fact]
    public async Task Photos_use_the_vision_model_with_strict_schema_and_no_tools()
    {
        var groq=new FakeGroq(JsonSerializer.Serialize(Valid,ReceiptCandidate.Json));
        var result=await Extract(groq,"image/png",[0x89,0x50,0x4E,0x47,1,2,3]);
        Assert.Equal(Valid,result.Candidate);
        Assert.Equal(("Groq","synthetic/vision"),(result.Provider,result.Model));
        var (request,body)=Assert.Single(groq.Calls);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions",request.RequestUri!.ToString());
        Assert.Equal("Bearer synthetic-groq-key",request.Headers.Authorization!.ToString());
        var root=body.RootElement;
        Assert.Equal("synthetic/vision",root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("tools",out _));
        Assert.True(root.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var image=root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString();
        Assert.StartsWith("data:image/png;base64,",image);
        Assert.Contains("untrusted",root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Pdfs_send_their_text_layer_to_the_text_model_as_untrusted_data()
    {
        var groq=new FakeGroq(JsonSerializer.Serialize(Valid,ReceiptCandidate.Json));
        var result=await Extract(groq,"application/pdf",TextPdf());
        Assert.Equal("synthetic/text",result.Model);
        var body=Assert.Single(groq.Calls).Body.RootElement;
        Assert.Equal("synthetic/text",body.GetProperty("model").GetString());
        var text=body.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("<<<RECEIPT",text);
        Assert.Contains("Total paid AUD 110.00",text);
    }

    /// <summary>An RGB PNG built by hand, so the test needs no imaging library.</summary>
    private static byte[] Png(int width,int height)
    {
        static uint Crc(byte[] data){uint crc=0xFFFFFFFF;foreach(var b in data){crc^=b;for(var k=0;k<8;k++)crc=(crc&1)!=0?0xEDB88320^(crc>>1):crc>>1;}return ~crc;}
        static byte[] BigEndian(uint value)=>[(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value];
        using var png=new MemoryStream();png.Write([137,80,78,71,13,10,26,10]);
        void Chunk(string type,byte[] data){png.Write(BigEndian((uint)data.Length));var typed=Encoding.ASCII.GetBytes(type).Concat(data).ToArray();png.Write(typed);png.Write(BigEndian(Crc(typed)));}
        Chunk("IHDR",[..BigEndian((uint)width),..BigEndian((uint)height),8,2,0,0,0]);
        using var pixels=new MemoryStream();
        using(var zlib=new System.IO.Compression.ZLibStream(pixels,System.IO.Compression.CompressionLevel.Fastest,true))
            for(var y=0;y<height;y++){zlib.WriteByte(0);for(var x=0;x<width;x++)zlib.Write([(byte)(x%256),(byte)(y%256),200]);}
        Chunk("IDAT",pixels.ToArray());Chunk("IEND",[]);
        return png.ToArray();
    }
    private static byte[] ScannedPdf()
    {
        using var pdf=new PdfDocument();var page=pdf.AddPage();
        using(var graphics=XGraphics.FromPdfPage(page))using(var image=XImage.FromStream(new MemoryStream(Png(420,600))))
            graphics.DrawImage(image,20,20,400,580);
        using var stream=new MemoryStream();pdf.Save(stream);return stream.ToArray();
    }

    [Fact]
    public async Task Scanned_pdfs_send_their_page_images_to_the_vision_model()
    {
        var groq=new FakeGroq(JsonSerializer.Serialize(Valid,ReceiptCandidate.Json));
        var result=await Extract(groq,"application/pdf",ScannedPdf());
        Assert.Equal(("Groq","synthetic/vision"),(result.Provider,result.Model));
        var content=Assert.Single(groq.Calls).Body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal(2,content.GetArrayLength());
        Assert.Matches("^data:image/(png|jpeg);base64,",content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Unreadable_inputs_and_missing_consent_fall_back_to_manual_without_calling_groq()
    {
        var groq=new FakeGroq("{}");
        Assert.Equal("Manual",(await Extract(groq,"application/pdf",Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"))).Provider);
        Assert.Equal("Manual",(await Extract(groq,"image/jpeg",new byte[ReceiptExtractor.MaxGroqImageBytes+1])).Provider);
        Assert.Equal("Manual",(await Extract(groq,"image/jpeg",[1,2,3],consent:false)).Provider);
        Assert.Empty(groq.Calls);
    }

    [Fact]
    public async Task Incomplete_output_is_rejected_and_untraceable_fields_are_blanked()
    {
        await Assert.ThrowsAsync<InvalidDataException>(()=>Extract(new FakeGroq(JsonSerializer.Serialize(Valid,ReceiptCandidate.Json),"length"),"image/png",[1]));
        // An untraceable field is blanked; the verified fields are kept for the person to check.
        var invented=JsonSerializer.Serialize(Valid with{Merchant=new("Invented",1m,null)},ReceiptCandidate.Json);
        var partial=(await Extract(new FakeGroq(invented),"image/png",[1])).Candidate;
        Assert.Null(partial.Merchant.Value);Assert.Equal(Valid.Amount,partial.Amount);Assert.Equal(Valid.Date,partial.Date);
        await Assert.ThrowsAnyAsync<Exception>(()=>Extract(new FakeGroq("not json"),"image/png",[1]));
    }

    [Fact]
    public async Task Rate_limits_surface_the_providers_retry_hint()
    {
        var limited=await Assert.ThrowsAsync<ProviderRateLimitedException>(()=>Extract(new FakeGroq("{}",status:HttpStatusCode.TooManyRequests),"image/png",[1]));
        Assert.Equal(TimeSpan.FromSeconds(7),limited.RetryAfter);
    }

    [Fact]
    public void Groq_needs_a_reviewed_text_model_and_text_model_changes_require_fresh_consent()
    {
        Assert.True(AiPrivacy.Configured(Config()));
        Assert.False(AiPrivacy.Configured(Config(new(){["Ai:TextModel"]=""})));
        Assert.False(AiPrivacy.Configured(Config(new(){["Ai:Endpoint"]="http://api.groq.com/openai/v1/chat/completions"})));
        Assert.NotEqual(AiPrivacy.NoticeVersion(Config()),AiPrivacy.NoticeVersion(Config(new(){["Ai:TextModel"]="synthetic/other"})));
        Assert.Equal(AiPrivacy.NoticeVersion(Config()),AiPrivacy.NoticeVersion(Config(new(){["Ai:ApiKey"]="rotated-key"})));
        Assert.Equal("https://console.groq.com/docs/your-data",AiPrivacy.PrivacyUrl(Config()));
    }
}
