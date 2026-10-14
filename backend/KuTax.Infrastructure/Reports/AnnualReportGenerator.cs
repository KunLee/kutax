using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using KuTax.Core.Claims;
using KuTax.Core.Receipts;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace KuTax.Infrastructure.Reports;

/// <summary>
/// Turns a frozen snapshot into the tax pack: a one-page summary, an item-by-item myTax entry guide and a spreadsheet,
/// in English and Chinese, plus the evidence archive. Every figure comes from the snapshot; nothing is recalculated.
/// </summary>
public sealed class AnnualReportGenerator(IDocumentStorage storage) : IAnnualReportGenerator
{
    private static readonly CultureInfo Australian=CultureInfo.GetCultureInfo("en-AU");
    public const long MaxEvidenceBytes=2L*1024*1024*1024;
    private static readonly Color Ink=Color.FromRgb(6,47,82), Soft=Color.FromRgb(90,105,120), Band=Color.FromRgb(232,245,240), Line=Color.FromRgb(200,210,215);

    public async Task<ExportFile[]> GenerateAsync(AnnualTaxSnapshot snapshot,string snapshotSha256,CancellationToken ct)
    {
        if(snapshot.Evidence.Length>2000||snapshot.Evidence.Sum(e=>e.Size)>MaxEvidenceBytes)throw new InvalidDataException("Evidence package limit exceeded.");
        var root=Path.Combine(Path.GetTempPath(),"kutax-export-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var files=new List<ExportFile>();
        var year=$"FY{snapshot.FinancialYearStart}-{(snapshot.FinancialYearStart+1)%100:00}";
        try
        {
            var jobs=ReportText.Languages.Select(language=>ReportText.For(language)).SelectMany(text=>new[]{"summary","guide","workbook"}.Select(kind=>(Text:text,Kind:kind)))
                .Append((Text:ReportText.For("en"),Kind:"evidence"));
            foreach(var (text,kind) in jobs)
            {
                ct.ThrowIfCancellationRequested();
                var name=text.FileName(kind,year);
                var path=Path.Combine(root,Guid.NewGuid().ToString("N")+Path.GetExtension(name));
                switch(kind)
                {
                    case "summary":WriteSummary(snapshot,text,path);break;
                    case "guide":WriteGuide(snapshot,text,path);break;
                    case "workbook":WriteWorkbook(snapshot,path,text);break;
                    default:await WriteEvidence(snapshot,snapshotSha256,path,ct);break;
                }
                var type=kind switch{"summary" or "guide"=>"application/pdf","workbook"=>"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",_=>"application/zip"};
                var key=$"exports/{Guid.NewGuid():N}";
                await using var content=File.OpenRead(path);var hash=Convert.ToHexString(await SHA256.HashDataAsync(content,ct));content.Position=0;
                // Register before Put so partially successful uploads are also cleaned on failure.
                files.Add(new(text.Kind(kind),name,type,key,content.Length,hash));
                await storage.PutAsync(key,content,type,ct);
            }
            return [..files];
        }
        catch
        {
            foreach(var file in files)try{await storage.DeleteAsync(file.ObjectKey,CancellationToken.None);}catch{/* Opaque orphan keys can be reconciled by the operator. */}
            throw;
        }
        finally{Directory.Delete(root,true);}
    }

    private static string Money(decimal value)=>value.ToString("C2",Australian);
    private static string Dollars(decimal value)=>value.ToString("C0",Australian);

    /// <summary>The pack's myTax labels; packs frozen before multi-label support had only D5 E.</summary>
    public static MyTaxSection[] SectionsOf(AnnualTaxSnapshot snapshot)=>snapshot.Assessment.Sections??[new("Other work-related expenses","D5 E",null,snapshot.Assessment.ApprovedTotal,snapshot.Assessment.EntryAmount,snapshot.Assessment.Rows.Count(r=>r.Status=="Approved"),[..snapshot.Assessment.Rows.Where(r=>r.Status=="Approved").Select(r=>r.Category).Distinct()])];
    private static TaxRule? RuleOf(AnnualTaxSnapshot snapshot,string? version)=>snapshot.Rules.Select(r=>r.Rule).FirstOrDefault(r=>r.Version==version)??TaxRuleCatalog.Rules.FirstOrDefault(r=>r.Version==version);
    private static string Label(AnnualTaxSnapshot snapshot,string category)=>snapshot.Rules.Select(r=>r.Rule).FirstOrDefault(r=>r.Category==category&&r.Label.Length>0)?.Label??category switch{"Unsupported"=>"Needs advice","Excluded"=>"Excluded",_=>category};
    /// <summary>Approved claims of one myTax section, oldest first.</summary>
    private static ClaimRow[] Items(AnnualTaxSnapshot snapshot,MyTaxSection section)=>[..snapshot.Assessment.Rows
        .Where(r=>r.Status=="Approved"&&(RuleOf(snapshot,r.RuleVersion) is {} rule?rule.PaperReference==section.PaperReference:section.PaperReference=="D5 E"))
        .OrderBy(r=>r.Date).ThenBy(r=>r.Merchant)];
    /// <summary>A short English description to type into myTax, e.g. "Anthropic, PBC – Software and subscriptions (40% work use)".</summary>
    private static string Describe(AnnualTaxSnapshot snapshot,ClaimRow row)
    {
        var what=string.Equals(row.Description.Trim(),row.Merchant.Trim(),StringComparison.OrdinalIgnoreCase)?$"{row.Merchant} – {Label(snapshot,row.Category)}":$"{row.Merchant} – {row.Description}";
        if(row.WorkUsePercent<100)what+=$" ({row.WorkUsePercent:0.##}% work use)";
        return what.Length>120?what[..119]+"…":what;
    }
    /// <summary>The receipt's file name inside the evidence ZIP, so the guide and spreadsheet point at the right file.</summary>
    private static string Receipt(AnnualTaxSnapshot snapshot,ClaimRow row)
    {
        var id=row.Evidence.Any(e=>e.Id==row.DocumentId)?row.DocumentId:row.Evidence.FirstOrDefault()?.Id;
        return id is Guid found&&EvidenceNames(snapshot).TryGetValue(found,out var path)?Path.GetFileName(path):"";
    }

    // ---------- PDF ----------
    private static Document BaseDocument(AnnualTaxSnapshot snapshot,ReportText text,string title,string plainTitle)
    {
        ReportFonts.Initialise();var document=new Document();document.Info.Title=plainTitle;document.Info.Author="KuTax";
        var normal=document.Styles[StyleNames.Normal]!;normal.Font.Name=text.Font;normal.Font.Size=10;normal.Font.Color=Ink;normal.ParagraphFormat.SpaceAfter=Unit.FromPoint(6);
        foreach(var (name,size) in new[]{(StyleNames.Heading1,24d),(StyleNames.Heading2,14d),(StyleNames.Heading3,11d)})
        {var style=document.Styles[name]!;style.Font.Name=text.Font;style.Font.Size=size;style.Font.Bold=true;style.ParagraphFormat.SpaceBefore=Unit.FromPoint(name==StyleNames.Heading1?2:14);style.ParagraphFormat.SpaceAfter=Unit.FromPoint(6);style.ParagraphFormat.KeepWithNext=true;}
        var section=document.AddSection();var page=section.PageSetup;page.PageFormat=PageFormat.A4;page.TopMargin=Unit.FromCentimeter(1.6);page.BottomMargin=Unit.FromCentimeter(1.7);page.LeftMargin=page.RightMargin=Unit.FromCentimeter(1.8);
        var footer=section.Footers.Primary.AddParagraph();footer.Format.Font.Size=7.5;footer.Format.Font.Color=Soft;footer.AddText(text["footer"]);footer.AddPageField();footer.AddText(text["of"]);footer.AddNumPagesField();if(text.Chinese)footer.AddText(" 页");
        var brand=section.AddParagraph("KuTax");brand.Format.Font.Bold=true;brand.Format.Font.Color=Color.FromRgb(16,140,150);brand.Format.SpaceAfter=0;
        section.AddParagraph(title,StyleNames.Heading1);
        var meta=section.AddParagraph(text.Format("year",snapshot.FinancialYearStart,(snapshot.FinancialYearStart+1)%100,snapshot.FinancialYearStart+1)+" · "+text.Format("prepared",snapshot.ApprovedAt.ToString(text.Chinese?"yyyy-MM-dd":"d MMM yyyy",Australian)));
        meta.Format.Font.Color=Soft;meta.Format.Font.Size=9;
        return document;
    }
    private static Table NewTable(Section section,params double[] widths)
    {
        var table=section.AddTable();table.Borders.Color=Line;table.Borders.Width=0.5;table.TopPadding=table.BottomPadding=Unit.FromPoint(4);table.LeftPadding=table.RightPadding=Unit.FromPoint(5);
        foreach(var width in widths)table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }
    private static Row AddRow(Table table,string[] values,bool heading=false,bool bold=false,int[]? right=null)
    {
        var row=table.AddRow();
        for(var i=0;i<values.Length;i++){var p=row.Cells[i].AddParagraph(values[i]??"");p.Format.SpaceAfter=0;if(right?.Contains(i)==true)p.Format.Alignment=ParagraphAlignment.Right;}
        if(heading){row.HeadingFormat=true;row.Shading.Color=Band;row.Format.Font.Bold=true;row.Format.Font.Size=8.5;}
        if(bold)row.Format.Font.Bold=true;
        return row;
    }
    private static void Save(Document document,AnnualTaxSnapshot snapshot,string path)
    {
        var renderer=new PdfDocumentRenderer{Document=document};renderer.RenderDocument();renderer.PdfDocument.Info.CreationDate=snapshot.ApprovedAt.UtcDateTime;renderer.PdfDocument.Save(path);
    }

    /// <summary>One page: what you can claim, what to enter where, and what was left out.</summary>
    private static void WriteSummary(AnnualTaxSnapshot snapshot,ReportText text,string path)
    {
        var report=snapshot.Assessment;var document=BaseDocument(snapshot,text,text["summary.title"],text.Plain("summary.title"));var section=document.LastSection;
        var hero=NewTable(section,17.4);hero.Borders.Visible=false;var heroRow=hero.AddRow();heroRow.Shading.Color=Band;heroRow.TopPadding=heroRow.BottomPadding=Unit.FromPoint(10);
        var cell=heroRow.Cells[0];var label=cell.AddParagraph(text["claim"]);label.Format.Font.Size=9;label.Format.Font.Color=Soft;
        var amount=cell.AddParagraph(Money(report.ApprovedTotal));amount.Format.Font.Size=26;amount.Format.Font.Bold=true;amount.Format.SpaceAfter=Unit.FromPoint(2);
        var note=cell.AddParagraph(text["claim.note"]);note.Format.Font.Size=8.5;note.Format.Font.Color=Soft;

        section.AddParagraph(text["enter.title"],StyleNames.Heading2);
        var sections=SectionsOf(snapshot);
        var table=NewTable(section,10.4,3.0,1.8,2.2);
        AddRow(table,[text["col.section"],text["col.claimType"],text["col.claims"],text["col.amount"]],heading:true,right:[2,3]);
        foreach(var item in sections)AddRow(table,[text.Section(item.Section,item.PaperReference),item.ClaimType??"–",item.Claims.ToString(CultureInfo.InvariantCulture),Dollars(item.EntryAmount)],right:[2,3]);
        var whole=section.AddParagraph(text["whole"]);whole.Format.Font.Size=8;whole.Format.Font.Color=Soft;whole.Format.SpaceBefore=Unit.FromPoint(4);

        var categories=report.Rows.Where(r=>r.Status=="Approved").GroupBy(r=>r.Category).OrderByDescending(g=>g.Sum(r=>r.DeductibleAmount??0)).ToArray();
        if(categories.Length>1)
        {
            section.AddParagraph(text["bycategory"],StyleNames.Heading2);
            var byCategory=NewTable(section,12.2,2.2,3.0);
            AddRow(byCategory,[text["col.category"],text["col.claims"],text["col.amount"]],heading:true,right:[1,2]);
            foreach(var group in categories)AddRow(byCategory,[text.Category(group.Key,Label(snapshot,group.Key)),group.Count().ToString(CultureInfo.InvariantCulture),Money(group.Sum(r=>r.DeductibleAmount??0))],right:[1,2]);
        }

        section.AddParagraph(text["left.title"],StyleNames.Heading2);
        var open=report.Rows.Where(r=>r.Status is not("Approved" or "Excluded")).ToArray();var excluded=report.Rows.Where(r=>r.Status=="Excluded").ToArray();
        if(open.Length==0&&excluded.Length==0&&report.Issues.Length==0)section.AddParagraph(text["left.none"]);
        if(open.Length>0)section.AddParagraph("• "+text.Format(open.Length==1?"left.advice.one":"left.advice",open.Length,Money(open.Sum(r=>r.ExpenseAmount))));
        if(excluded.Length>0)section.AddParagraph("• "+text.Format(excluded.Length==1?"left.excluded.one":"left.excluded",excluded.Length,Money(excluded.Sum(r=>r.ExpenseAmount))));
        if(report.Issues.Length>0)section.AddParagraph("• "+text.Format(report.Issues.Length==1?"left.issues.one":"left.issues",report.Issues.Length));
        var disclaimer=section.AddParagraph(text["disclaimer"]);disclaimer.Format.Font.Size=8;disclaimer.Format.Font.Color=Soft;disclaimer.Format.SpaceBefore=Unit.FromPoint(14);
        Save(document,snapshot,path);
    }

    /// <summary>Section by section, every receipt to enter: date, the description to type, the amount, and a box to tick.</summary>
    private static void WriteGuide(AnnualTaxSnapshot snapshot,ReportText text,string path)
    {
        var document=BaseDocument(snapshot,text,text["guide.title"],text.Plain("guide.title"));var section=document.LastSection;
        section.AddParagraph(text["guide.intro"]);
        foreach(var (step,i) in new[]{"guide.step1","guide.step2","guide.step3","guide.step4"}.Select((s,i)=>(s,i)))
        {var p=section.AddParagraph($"{i+1}. {text[step]}");p.Format.LeftIndent=Unit.FromCentimeter(0.4);p.Format.SpaceAfter=Unit.FromPoint(3);}
        foreach(var item in SectionsOf(snapshot))
        {
            section.AddParagraph(text.Section(item.Section,item.PaperReference),StyleNames.Heading2);
            var total=section.AddParagraph(text.Format("guide.section",Dollars(item.EntryAmount)));total.Format.Font.Bold=true;total.Format.SpaceAfter=Unit.FromPoint(2);
            if(item.ClaimType is {} code){var claimType=section.AddParagraph(text.Format("guide.claimType",text.ClaimType(code)));claimType.Format.SpaceAfter=Unit.FromPoint(4);}
            var table=NewTable(section,1.1,2.5,7.4,2.3,4.1);
            AddRow(table,[text["col.done"],text["col.date"],text["col.describe"],text["col.amount"],text["col.receipt"]],heading:true,right:[3]);
            foreach(var row in Items(snapshot,item))
            {
                var details=Describe(snapshot,row);
                if(row.WorkReimbursement>0)details+=" · "+text.Format("guide.reimbursed",Money(row.WorkReimbursement));
                var r=AddRow(table,["",row.Date.ToString(text.Chinese?"yyyy-MM-dd":"d MMM yyyy",Australian),details,Money(row.DeductibleAmount??0),Receipt(snapshot,row)],right:[3]);
                // A small empty box to tick on paper (the fonts have no ballot-box glyph).
                var box=(MigraDoc.DocumentObjectModel.Paragraph)r.Cells[0].Elements[0]!;box.Format.Borders.Width=0.8;box.Format.Borders.Color=Ink;box.Format.LeftIndent=Unit.FromCentimeter(0.15);box.Format.RightIndent=Unit.FromCentimeter(0.15);box.Format.Font.Size=8;box.AddText(" ");
                r.Cells[4].Format.Font.Size=7.5;r.Cells[4].Format.Font.Color=Soft;
                // Merchant on one line, "date_amount_category.pdf" on the next, so a long name never splits inside the date.
                var file=Receipt(snapshot,row);var date=System.Text.RegularExpressions.Regex.Match(file,@"_\d{4}-\d{2}-\d{2}_");var split=date.Success?date.Index:-1;
                if(split>0){var name=(MigraDoc.DocumentObjectModel.Paragraph)r.Cells[4].Elements[0]!;name.Elements.Clear();name.AddText(file[..(split+1)]);name.AddLineBreak();name.AddText(file[(split+1)..]);}
            }
            AddRow(table,["","",text["col.section.total"],Money(item.ApprovedTotal),""],bold:true,right:[2,3]).Shading.Color=Band;
        }
        var disclaimer=section.AddParagraph(text["disclaimer"]);disclaimer.Format.Font.Size=8;disclaimer.Format.Font.Color=Soft;disclaimer.Format.SpaceBefore=Unit.FromPoint(14);
        Save(document,snapshot,path);
    }

    // ---------- Spreadsheet ----------
    /// <summary>
    /// Summary first (cell B2 is the approved total), then the myTax entries, what wasn't claimed, every expense
    /// (deductible in column 9, status in 10), the rules and the receipt files. Only readable columns: IDs, versions
    /// and checksums stay in the evidence ZIP's manifest.json and snapshot.json for anyone verifying the pack.
    /// </summary>
    public static void WriteWorkbook(AnnualTaxSnapshot snapshot,string path,ReportText? language=null)
    {
        var text=ReportText.For(language?.Language??"en",forPdf:false);
        using var workbook=new XLWorkbook();var report=snapshot.Assessment;
        workbook.Style.Font.FontName=text.Chinese?"Microsoft YaHei":"Calibri";

        var summary=workbook.AddWorksheet(text["sheet.summary"]);
        var rows=new (string,object?)[]{
            (text["sum.year"],$"FY {snapshot.FinancialYearStart}–{(snapshot.FinancialYearStart+1)%100:00}"),
            (text["sum.approved"],report.ApprovedTotal),(text["sum.entry"],report.EntryAmount),(text["sum.claims"],report.Rows.Count(r=>r.Status=="Approved")),
            (text["sum.waiting"],report.CandidateTotal),(text["sum.unassessed"],report.UnassessedExpenseTotal),(text["sum.excluded"],report.ExcludedExpenseTotal),
            (text["sum.prepared"],snapshot.ApprovedAt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),(text["sum.note"],snapshot.ApprovalNote)};
        // Row 1 is the year so the approved total lands in B2 (relied on by reconciliation checks).
        for(var i=0;i<rows.Length;i++){summary.Cell(i+1,1).Value=rows[i].Item1;SetValue(summary.Cell(i+1,2),rows[i].Item2);}
        summary.Range(2,2,3,2).Style.NumberFormat.Format="$#,##0.00";summary.Range(5,2,7,2).Style.NumberFormat.Format="$#,##0.00";
        summary.Cell(2,1).Style.Font.Bold=true;summary.Cell(2,2).Style.Font.Bold=true;summary.Cell(2,2).Style.Font.FontSize=14;summary.Range(2,1,2,2).Style.Fill.BackgroundColor=XLColor.FromHtml("E8F5F0");
        var sectionsAt=rows.Length+2;summary.Cell(sectionsAt,1).Value=text["enter.title"];summary.Cell(sectionsAt,1).Style.Font.Bold=true;
        foreach(var (item,i) in SectionsOf(snapshot).Select((s,i)=>(s,i)))
        {summary.Cell(sectionsAt+1+i,1).Value=text.Section(item.Section,item.PaperReference)+(item.ClaimType is null?"":$" · {text["col.claimType"]} {item.ClaimType}");summary.Cell(sectionsAt+1+i,2).Value=(double)item.EntryAmount;summary.Cell(sectionsAt+1+i,2).Style.NumberFormat.Format="$#,##0";}
        summary.Column(1).Width=62;summary.Column(2).Width=22;summary.Column(1).Style.Alignment.WrapText=true;summary.Column(2).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Left;

        var entries=workbook.AddWorksheet(text["sheet.entries"]);
        string[] entryHeaders=[text["col.section"],text["col.claimType"],text["col.date"],text["col.describe"],text["col.expense"],text["col.workUse"],text["col.reimbursed"],text["col.deductible"],text["col.receipt"]];
        for(var i=0;i<entryHeaders.Length;i++)entries.Cell(1,i+1).Value=entryHeaders[i];
        var at=2;
        foreach(var item in SectionsOf(snapshot))foreach(var row in Items(snapshot,item))
        {
            object?[] values=[text.Section(item.Section,item.PaperReference),row.ClaimType,row.Date.ToDateTime(TimeOnly.MinValue),Describe(snapshot,row),row.ExpenseAmount,row.WorkUsePercent/100m,row.WorkReimbursement,row.DeductibleAmount,Receipt(snapshot,row)];
            for(var i=0;i<values.Length;i++)SetValue(entries.Cell(at,i+1),values[i]);at++;
        }
        Finish(entries,at-1,entryHeaders.Length,dateColumns:[3],moneyColumns:[5,7,8],percentColumns:[6],widths:[42,10,13,52,13,11,13,13,30]);
        if(at>2){entries.Cell(at,7).Value=text["col.section.total"];SetValue(entries.Cell(at,8),report.ApprovedTotal);entries.Cell(at,8).Style.NumberFormat.Format="$#,##0.00";entries.Row(at).Style.Font.Bold=true;}

        var left=workbook.AddWorksheet(text["sheet.left"]);
        string[] leftHeaders=[text["col.date"],text["col.merchant"],text["col.category"],text["col.expense"],text["col.status"],text["col.why"]];
        for(var i=0;i<leftHeaders.Length;i++)left.Cell(1,i+1).Value=leftHeaders[i];
        at=2;
        foreach(var row in report.Rows.Where(r=>r.Status!="Approved").OrderBy(r=>r.Date))
        {object?[] values=[row.Date.ToDateTime(TimeOnly.MinValue),row.Merchant,text.Category(row.Category,Label(snapshot,row.Category)),row.ExpenseAmount,text.Status(row.Status),row.Reason];for(var i=0;i<values.Length;i++)SetValue(left.Cell(at,i+1),values[i]);at++;}
        foreach(var issue in report.Issues){SetValue(left.Cell(at,4),issue.Amount);left.Cell(at,6).Value=issue.Description;at++;}
        Finish(left,at-1,leftHeaders.Length,dateColumns:[1],moneyColumns:[4],percentColumns:[],widths:[13,30,22,13,18,70]);

        var names=EvidenceNames(snapshot);
        var all=workbook.AddWorksheet(text["sheet.details"]);
        string[] allHeaders=[text["col.date"],text["col.merchant"],text["col.description"],text["col.category"],text["col.invoice"],text["col.expense"],text["col.workUse"],text["col.reimbursed"],text["col.deductible"],text["col.status"],text["col.why"],text["col.section"],text["col.claimType"],text["col.foreign"],text["col.receipt"]];
        for(var i=0;i<allHeaders.Length;i++)all.Cell(1,i+1).Value=allHeaders[i];
        at=2;foreach(var row in report.Rows.OrderBy(r=>r.Date).ThenBy(r=>r.Merchant,StringComparer.Ordinal))
        {
            var rule=RuleOf(snapshot,row.RuleVersion);
            var foreign=row.OriginalCurrency is null?null:$"{row.OriginalCurrency} {row.OriginalAmount:0.00} · "+text[row.ConversionSource=="RBA"?"fx.rba":"fx.statement"];
            object?[] values=[row.Date.ToDateTime(TimeOnly.MinValue),row.Merchant,row.Description,text.Category(row.Category,Label(snapshot,row.Category)),row.InvoiceAmount,row.ExpenseAmount,row.WorkUsePercent/100m,row.WorkReimbursement,row.DeductibleAmount,text.Status(row.Status),row.Reason,
                rule is null?null:text.Section(rule.Section,rule.PaperReference),row.ClaimType,foreign,Receipt(snapshot,row)];
            for(var i=0;i<values.Length;i++)SetValue(all.Cell(at,i+1),values[i]);at++;
        }
        Finish(all,at-1,allHeaders.Length,dateColumns:[1],moneyColumns:[5,6,8,9],percentColumns:[7],widths:[13,28,32,22,13,13,10,13,13,16,50,36,10,26,34]);

        var rules=workbook.AddWorksheet(text["sheet.rules"]);string[] ruleHeaders=[text["col.category"],text["col.section"],text["col.conditions"],text["col.source"],text["col.reviewed"],text["col.note"]];
        for(var i=0;i<ruleHeaders.Length;i++)rules.Cell(1,i+1).Value=ruleHeaders[i];at=2;
        foreach(var item in snapshot.Rules){object?[] values=[text.Category(item.Rule.Category,item.Rule.Label),text.Section(item.Rule.Section,item.Rule.PaperReference),item.Rule.Conditions,item.Rule.SourceUrl,item.ReviewedAt.UtcDateTime.Date,item.Note];for(var i=0;i<values.Length;i++)SetValue(rules.Cell(at,i+1),values[i]);at++;}
        Finish(rules,at-1,ruleHeaders.Length,dateColumns:[5],moneyColumns:[],percentColumns:[],widths:[24,36,90,40,13,40]);

        var evidence=workbook.AddWorksheet(text["sheet.evidence"]);string[] evidenceHeaders=[text["col.file"],text["col.original"],text["col.size"]];
        for(var i=0;i<evidenceHeaders.Length;i++)evidence.Cell(1,i+1).Value=evidenceHeaders[i];at=2;
        foreach(var item in snapshot.Evidence.OrderBy(e=>names[e.Id],StringComparer.OrdinalIgnoreCase)){evidence.Cell(at,1).Value=Path.GetFileName(names[item.Id]);evidence.Cell(at,2).Value=item.Name;evidence.Cell(at,3).Value=Math.Max(1,(item.Size+512)/1024);at++;}
        Finish(evidence,at-1,evidenceHeaders.Length,dateColumns:[],moneyColumns:[],percentColumns:[],widths:[48,40,12]);

        summary.SetTabActive();
        workbook.Properties.Title="KuTax annual deductions - frozen snapshot";workbook.Properties.Author="KuTax";workbook.SaveAs(path);
    }
    private static void SetValue(IXLCell cell,object? value)
    {
        switch(value){case null:break;case decimal amount:cell.Value=(double)amount;break;case int number:cell.Value=number;break;case long number:cell.Value=number;break;case DateTime date:cell.Value=date;break;default:cell.Value=value.ToString()??"";break;}
    }
    private static void Finish(IXLWorksheet sheet,int rows,int columns,int[] dateColumns,int[] moneyColumns,int[] percentColumns,double[]? widths)
    {
        sheet.Range(1,1,Math.Max(2,rows),columns).CreateTable().Theme=XLTableTheme.TableStyleLight9;
        sheet.SheetView.FreezeRows(1);sheet.Row(1).Style.Font.Bold=true;sheet.Row(1).Height=30;sheet.Rows().Style.Alignment.Vertical=XLAlignmentVerticalValues.Top;
        for(var i=1;i<=columns;i++)sheet.Column(i).Width=widths is not null&&i<=widths.Length?widths[i-1]:24;
        sheet.Columns(1,columns).Style.Alignment.WrapText=true;
        foreach(var c in dateColumns)sheet.Column(c).Style.DateFormat.Format="dd mmm yyyy";
        foreach(var c in moneyColumns)sheet.Column(c).Style.NumberFormat.Format="$#,##0.00";
        foreach(var c in percentColumns)sheet.Column(c).Style.NumberFormat.Format="0%";
        sheet.PageSetup.PageOrientation=XLPageOrientation.Landscape;sheet.PageSetup.FitToPages(1,0);sheet.PageSetup.SetRowsToRepeatAtTop(1,1);
    }
    private static readonly ConditionalWeakTable<AnnualTaxSnapshot,Dictionary<Guid,string>> Names=new();

    /// <summary>
    /// Archive paths named after the receipt: <c>originals/Merchant_2025-08-01_AUD 123.45_Category.pdf</c> (merchant,
    /// receipt date, invoice total in the invoice's currency, category). A supporting document takes its receipt's name plus "_supporting"; repeats get " (2)".
    /// </summary>
    public static IReadOnlyDictionary<Guid,string> EvidenceNames(AnnualTaxSnapshot snapshot)=>Names.GetValue(snapshot,s=>
    {
        var taken=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var names=new Dictionary<Guid,string>();
        var rows=s.Assessment.Rows.OrderBy(r=>r.Date).ThenBy(r=>r.Merchant,StringComparer.Ordinal).ToArray();
        foreach(var evidence in s.Evidence)
        {
            var own=rows.FirstOrDefault(r=>r.DocumentId==evidence.Id);var supported=own??rows.FirstOrDefault(r=>r.Evidence.Any(e=>e.Id==evidence.Id));
            // Category last; a receipt split across categories names each one, in English like the myTax sections.
            var categories=string.Join("+",rows.Where(r=>supported is not null&&r.DocumentId==supported.DocumentId).Select(r=>ShortCategory(s,r.Category)).Distinct());
            var stem=supported is null?Safe(Path.GetFileNameWithoutExtension(evidence.Name)):
                $"{Safe(supported.Merchant)}_{supported.Date:yyyy-MM-dd}_{InvoiceMoney(supported)}"+(own is null?"_supporting":"")+"_"+Safe(categories);
            var extension=evidence.ContentType switch{"application/pdf"=>".pdf","image/png"=>".png","image/jpeg"=>".jpg",_=>".bin"};
            var name=stem+extension;for(var n=2;!taken.Add(name);n++)name=$"{stem} ({n}){extension}";
            names[evidence.Id]="originals/"+name;
        }
        return names;
    });

    /// <summary>The total as printed on the invoice: "USD 20.00" for a foreign receipt, otherwise "AUD 29.99".</summary>
    private static string InvoiceMoney(ClaimRow row)=>row.OriginalCurrency is {Length:3} currency&&row.OriginalAmount is decimal amount
        ?$"{currency} {amount.ToString("0.00",CultureInfo.InvariantCulture)}":$"AUD {row.InvoiceAmount.ToString("0.00",CultureInfo.InvariantCulture)}";

    private static string ShortCategory(AnnualTaxSnapshot snapshot,string category)=>category switch
    {
        "SelfEducation"=>"Self-education","SoftwareSubscriptions"=>"Software and subscriptions","ToolsEquipment"=>"Tools and equipment",
        "PhoneInternet"=>"Phone and internet","WorkClothing"=>"Work clothing","UnionFees"=>"Union fees","ProfessionalMembership"=>"Professional memberships",
        "Donations"=>"Gifts and donations","TaxAffairs"=>"Managing tax affairs",_=>Label(snapshot,category),
    };

    /// <summary>Keeps a merchant name usable as a file name on Windows, macOS and Linux.</summary>
    private static string Safe(string text)
    {
        var builder=new StringBuilder();
        foreach(var c in text.Normalize(NormalizationForm.FormC))builder.Append(char.IsControl(c)||"\\/:*?\"<>|".Contains(c)?'-':c);
        var safe=string.Join(' ',builder.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries)).TrimStart('.').Trim();
        if(safe.Length>60)safe=safe[..60].TrimEnd();
        return safe.Length==0?"Receipt":safe;
    }
    public static string EvidencePath(AnnualTaxSnapshot snapshot,EvidenceReference evidence)=>EvidenceNames(snapshot)[evidence.Id];
    private async Task WriteEvidence(AnnualTaxSnapshot snapshot,string hash,string path,CancellationToken ct)
    {
        await using var file=File.Create(path);using var zip=new ZipArchive(file,ZipArchiveMode.Create,true);
        await using(var manifest=zip.CreateEntry("manifest.json").Open())await JsonSerializer.SerializeAsync(manifest,new{snapshot.Id,snapshot.ModelVersion,snapshot.FinancialYearStart,snapshot.ApprovedAt,snapshot.ApprovedBy,snapshotSha256=hash,approvedTotal=snapshot.Assessment.ApprovedTotal,entryAmount=snapshot.Assessment.EntryAmount,sections=SectionsOf(snapshot),documents=snapshot.Evidence.Select(e=>new{e.Id,e.Name,e.Sha256,e.Size,path=EvidencePath(snapshot,e),allocations=snapshot.Assessment.Rows.Where(r=>r.Evidence.Any(d=>d.Id==e.Id)).Select(r=>new{r.AllocationId,r.ClaimId,r.Status,r.DeductibleAmount,r.RuleVersion})}),issues=snapshot.Assessment.Issues},ReceiptCandidate.Json,ct);
        await using(var model=zip.CreateEntry("snapshot.json").Open())await JsonSerializer.SerializeAsync(model,snapshot,ReceiptCandidate.Json,ct);
        foreach(var evidence in snapshot.Evidence)
        {
            ct.ThrowIfCancellationRequested();await using var source=await storage.OpenReadAsync(evidence.ObjectKey,ct);await using var target=zip.CreateEntry(EvidencePath(snapshot,evidence),CompressionLevel.Fastest).Open();
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long size=0;int count;
            while((count=await source.Content.ReadAsync(buffer,ct))>0){size+=count;if(size>evidence.Size)throw new InvalidDataException("Evidence size changed.");digest.AppendData(buffer,0,count);await target.WriteAsync(buffer.AsMemory(0,count),ct);}
            if(size!=evidence.Size||!Convert.ToHexString(digest.GetHashAndReset()).Equals(evidence.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Evidence checksum changed.");
        }
    }
}
