using PdfSharp.Fonts;

namespace KuTax.Infrastructure.Reports;
public sealed class ReportFonts : IFontResolver
{
    // "Noto Sans SC" carries Chinese (and Latin) glyphs for the Chinese files; everything else uses Noto Sans.
    public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic)=>familyName.Contains("SC",StringComparison.Ordinal)
        ?new(bold?"NotoSansSC-Bold":"NotoSansSC-Regular",false,italic):new(bold?"NotoSans-Bold":"NotoSans-Regular",false,italic);
    public byte[] GetFont(string faceName)
    {
        if(faceName is not("NotoSans-Regular" or "NotoSans-Bold" or "NotoSansSC-Regular" or "NotoSansSC-Bold"))throw new InvalidOperationException("Unknown report font.");
        using var stream=typeof(ReportFonts).Assembly.GetManifestResourceStream($"KuTax.Infrastructure.Reports.Fonts.{faceName}.ttf")??throw new InvalidOperationException("Report font missing.");
        using var content=new MemoryStream();stream.CopyTo(content);return content.ToArray();
    }
    private static readonly object Gate=new();
    public static void Initialise(){lock(Gate){GlobalFontSettings.FontResolver??=new ReportFonts();}}
}
