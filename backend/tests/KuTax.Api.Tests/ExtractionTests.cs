using System.Text.Json;
using KuTax.Core.Receipts;

namespace KuTax.Api.Tests;
public sealed class ExtractionTests
{
    [Fact]
    public void Extraction_rejects_invented_invalid_and_untraceable_fields()
    {
        var valid=new ReceiptCandidate(new("Synthetic Union",.98m,"Page 1 merchant"),new("2025-08-01",.95m,"Page 1 date"),new(110m,.99m,"TOTAL 110.00"),new(10m,.95m,"GST 10.00"),"AUD");
        Assert.Equal(valid,ReceiptCandidate.Parse(JsonSerializer.Serialize(valid,ReceiptCandidate.Json)));
        foreach(var invalid in new[]{valid with{Amount=new(-1m,1m,"total")},valid with{Date=new("01/02/25",1m,"date")},valid with{Gst=new(200m,1m,"gst")},valid with{Merchant=new("Invented",1m,null)},valid with{Currency="USD"},valid with{Amount=new(1m,1.1m,"total")}})
            Assert.Throws<InvalidDataException>(()=>ReceiptCandidate.Parse(JsonSerializer.Serialize(invalid,ReceiptCandidate.Json)));
        Assert.Equal(ReceiptCandidate.Empty,ReceiptCandidate.Parse(JsonSerializer.Serialize(ReceiptCandidate.Empty,ReceiptCandidate.Json)));
    }

    [Fact]
    public void Sanitising_blanks_only_the_invalid_fields_and_still_rejects_malformed_payloads()
    {
        var today=new DateOnly(2026,10,11);
        var valid=new ReceiptCandidate(new("Synthetic Union",.98m,"Page 1 merchant"),new("2025-08-01",.95m,"Page 1 date"),new(110m,.99m,"TOTAL 110.00"),new(10m,.95m,"GST 10.00"),"AUD");
        var badDate=ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Date=new("01/02/25",1m,"date")},ReceiptCandidate.Json),today);
        Assert.Equal((valid.Merchant,valid.Amount,valid.Gst),(badDate.Merchant,badDate.Amount,badDate.Gst));
        Assert.Null(badDate.Date.Value);Assert.Equal(0m,badDate.Date.Confidence);
        Assert.Null(ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Date=new("2027-01-01",1m,"future")},ReceiptCandidate.Json),today).Date.Value);
        var untraceable=ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Merchant=new("Invented",1m,null)},ReceiptCandidate.Json),today);
        Assert.Null(untraceable.Merchant.Value);Assert.Equal(110m,untraceable.Amount.Value);
        // GST can't outlive an invalid amount.
        var badAmount=ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Amount=new(-1m,1m,"total")},ReceiptCandidate.Json),today);
        Assert.Null(badAmount.Amount.Value);Assert.Null(badAmount.Gst.Value);
        // A foreign-currency receipt: amounts left blank, the original currency kept as evidence.
        var usd=ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Amount=new(null,0m,"USD 5.00"),Gst=new(null,0m,null)},ReceiptCandidate.Json),today);
        Assert.Equal("USD 5.00",usd.Amount.Evidence);
        Assert.Throws<InvalidDataException>(()=>ReceiptCandidate.Sanitize(JsonSerializer.Serialize(valid with{Currency="USD"},ReceiptCandidate.Json),today));
        Assert.ThrowsAny<Exception>(()=>ReceiptCandidate.Sanitize("not json",today));
    }
}
