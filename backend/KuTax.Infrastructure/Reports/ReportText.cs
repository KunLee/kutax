using KuTax.Core.Claims;

namespace KuTax.Infrastructure.Reports;

/// <summary>
/// Wording for the tax-pack files in each language KuTax produces. myTax itself is in English, so the names of
/// myTax sections and the descriptions to type stay in English in every language, with a translation alongside.
/// </summary>
public sealed class ReportText
{
    public static readonly string[] Languages = ["en", "zh"];
    public string Language { get; }
    public bool Chinese => Language == "zh";
    public string Font => Chinese ? "Noto Sans SC" : "Noto Sans";
    private readonly Dictionary<string, string> text;
    /// <summary>PDF layout needs explicit break points in Chinese text; spreadsheets and file names must not get them.</summary>
    private readonly bool forPdf;
    private ReportText(string language, Dictionary<string, string> text, bool forPdf) { Language = language; this.text = text; this.forPdf = forPdf; }
    public static ReportText For(string language, bool forPdf = true) => language == "zh" ? new("zh", Zh, forPdf) : new("en", En, forPdf);
    public string this[string key] => Breakable(text.TryGetValue(key, out var value) ? value : En[key]);
    /// <summary>The text without layout break points, for document metadata such as the PDF title.</summary>
    public string Plain(string key) => text.TryGetValue(key, out var value) ? value : En[key];
    public string Format(string key, params object[] values) => string.Format(System.Globalization.CultureInfo.InvariantCulture, this[key], values);

    /// <summary>
    /// Chinese has no spaces, and the PDF layout only wraps at break opportunities, so a zero-width space follows each
    /// Chinese character or full-width punctuation mark. It's invisible and doesn't change copied text in a meaningful way.
    /// </summary>
    public string Breakable(string value)
    {
        if (!Chinese || !forPdf) return value;
        var builder = new System.Text.StringBuilder(value.Length * 2);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            builder.Append(c);
            // Break after a Chinese character or punctuation mark, but never just before punctuation (a line mustn't start with "，").
            var next = i + 1 < value.Length ? value[i + 1] : ' ';
            var nextIsPunctuation = next is >= '\u3000' and <= '\u303F' or >= '\uFF00' and <= '\uFFEF';
            if (!nextIsPunctuation && (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3000' and <= '\u303F' or >= '\uFF00' and <= '\uFFEF')) builder.Append('\u200B');
        }
        return builder.ToString();
    }

    /// <summary>File-kind suffix: English files keep the original kinds; other languages add "-zh".</summary>
    public string Kind(string kind) => Chinese && kind != "evidence" ? kind + "-zh" : kind;
    public string FileName(string kind, string year) => (Chinese, kind) switch
    {
        (true, "summary") => $"年度报税摘要_{year}.pdf", (true, "guide") => $"myTax填写指南_{year}.pdf", (true, "workbook") => $"抵扣明细_{year}.xlsx",
        (_, "summary") => $"Annual_Tax_Summary_{year}.pdf", (_, "guide") => $"myTax_Entry_Guide_{year}.pdf", (_, "workbook") => $"Deductions_{year}.xlsx",
        _ => $"Evidence_Package_{year}.zip",
    };

    /// <summary>A myTax section as shown in myTax, with a Chinese explanation in the Chinese files.</summary>
    public string Section(string section, string paperReference)
    {
        if (!Chinese) return $"{section} ({paperReference})";
        var zh = paperReference.Split(' ')[0] switch
        {
            "D3" => "与工作相关的服装、洗衣和干洗费用", "D4" => "与工作相关的自我教育费用", "D5" => "其他与工作相关的费用",
            "D9" => "礼物或捐赠", "D10" => "管理税务事务的费用", _ => null,
        };
        return zh is null ? $"{section} ({paperReference})" : Breakable($"{section} ({paperReference}) — {zh}");
    }

    public string Category(string category, string fallback) => Chinese ? Breakable(category switch
    {
        "SelfEducation" => "自我教育", "SoftwareSubscriptions" => "软件和订阅", "ToolsEquipment" => "工具和设备", "PhoneInternet" => "电话和网络",
        "WorkClothing" => "工作服装", "UnionFees" => "工会会费", "ProfessionalMembership" => "专业协会会费", "Donations" => "礼物和捐赠",
        "TaxAffairs" => "管理税务事务", "Unsupported" => "需要咨询", "Excluded" => "已排除", _ => fallback,
    }) : fallback;

    public string ClaimType(string code) => Breakable((Chinese, code) switch
    {
        (false, "K") => "K – maintains or improves skills for your current job", (true, "K") => "K – 保持或提升当前工作所需的技能",
        (false, "I") => "I – likely to increase income from your current job", (true, "I") => "I – 可能增加当前工作的收入",
        (false, "C") => "C – compulsory uniform", (true, "C") => "C – 强制性制服",
        (false, "N") => "N – non-compulsory registered uniform", (true, "N") => "N – 已登记的非强制制服",
        (false, "S") => "S – occupation-specific clothing", (true, "S") => "S – 职业专用服装",
        (false, "P") => "P – protective clothing", (true, "P") => "P – 防护服装",
        _ => code,
    });

    public string Status(string status) => Breakable((Chinese, status) switch
    {
        (true, "Approved") => "已批准", (true, "Candidate") => "待批准", (true, "Unsupported") => "需要咨询", (true, "Excluded") => "已排除",
        (true, "NeedsReview") => "缺少信息", (true, "NeedsEvidence") => "缺少证据", (true, "UnverifiedRule") => "规则待确认",
        (false, "Candidate") => "Waiting for approval", (false, "Unsupported") => "Needs advice", (false, "NeedsReview") => "Needs details",
        (false, "NeedsEvidence") => "Needs evidence", (false, "UnverifiedRule") => "Rule not checked", _ => status,
    });

    private static readonly Dictionary<string, string> En = new()
    {
        ["summary.title"] = "Annual tax summary",
        ["guide.title"] = "myTax entry guide",
        ["year"] = "FY {0}–{1:00} · 1 July {0} to 30 June {2}",
        ["prepared"] = "Prepared {0}",
        ["footer"] = "KuTax · tax preparation pack · not a lodged return · page ",
        ["of"] = " of ",
        ["claim"] = "Deductions you can claim",
        ["claim.note"] = "This lowers your taxable income. It isn't your refund: that depends on your tax rate and the tax already withheld, which myTax works out.",
        ["enter.title"] = "What to enter in myTax",
        ["col.section"] = "myTax section", ["col.claimType"] = "Claim type", ["col.amount"] = "Amount", ["col.claims"] = "Claims",
        ["col.category"] = "Category", ["col.date"] = "Date", ["col.describe"] = "Description to type", ["col.receipt"] = "Receipt",
        ["col.done"] = "Done", ["col.merchant"] = "Merchant", ["col.expense"] = "Expense", ["col.workUse"] = "Work use",
        ["col.reimbursed"] = "Reimbursed", ["col.deductible"] = "Deductible", ["col.status"] = "Status", ["col.why"] = "Why",
        ["col.section.total"] = "Section total",
        ["col.description"] = "Description", ["col.invoice"] = "Receipt total", ["col.foreign"] = "Foreign currency",
        ["col.conditions"] = "Conditions", ["col.source"] = "ATO source", ["col.reviewed"] = "Checked on", ["col.note"] = "Note",
        ["col.file"] = "File in the receipts ZIP", ["col.original"] = "Uploaded as", ["col.size"] = "Size (KB)",
        ["fx.rba"] = "RBA rate (estimate)", ["fx.statement"] = "bank or card statement",
        ["whole"] = "Enter whole dollars; the cents are dropped once per section.",
        ["bycategory"] = "By category",
        ["left.title"] = "Not included",
        ["left.none"] = "Nothing was left out.",
        ["left.advice"] = "{0} expenses ({1}) need advice or more details. They're listed in the spreadsheet.",
        ["left.excluded"] = "{0} expenses ({1}) were marked private or excluded.",
        ["left.issues"] = "{0} receipt notes, such as unallocated balances or missing dates.",
        ["left.advice.one"] = "1 expense ({1}) needs advice or more details. It's listed in the spreadsheet.",
        ["left.excluded.one"] = "1 expense ({1}) was marked private or excluded.",
        ["left.issues.one"] = "1 receipt note, such as an unallocated balance or a missing date.",
        ["disclaimer"] = "KuTax prepares figures from receipts you confirmed and rules an administrator checked against ATO sources. It doesn't lodge your return or give tax advice. Check anything you're unsure about with the ATO or a registered tax agent.",
        ["guide.intro"] = "Follow this guide in myTax. Each section lists the receipts behind it, so you can enter or check them one by one.",
        ["guide.step1"] = "Sign in to myGov, open the ATO and choose Prepare tax return.",
        ["guide.step2"] = "Go to Deductions and open each section below. Some amounts, like union fees, may already be prefilled: don't add them twice.",
        ["guide.step3"] = "Add each item with the description and amount shown (or enter the section total if myTax only asks for one), and choose the claim type where shown.",
        ["guide.step4"] = "Tick each line as you go. Keep this pack with your records for 5 years.",
        ["guide.section"] = "Section total to enter: {0}",
        ["guide.claimType"] = "Claim type: {0}",
        ["guide.workUse"] = "{0}% work use",
        ["guide.reimbursed"] = "less {0} reimbursed",
        ["sheet.summary"] = "Summary", ["sheet.entries"] = "myTax entries", ["sheet.left"] = "Not claimed", ["sheet.details"] = "All expenses",
        ["sheet.rules"] = "Rules", ["sheet.evidence"] = "Evidence",
        ["sum.approved"] = "Deductions you can claim", ["sum.entry"] = "Total of whole-dollar entries", ["sum.claims"] = "Approved claims",
        ["sum.waiting"] = "Calculated, not approved", ["sum.unassessed"] = "Needs advice or details", ["sum.excluded"] = "Excluded",
        ["sum.year"] = "Financial year", ["sum.prepared"] = "Prepared", ["sum.note"] = "Your note",
    };

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["summary.title"] = "年度报税摘要",
        ["guide.title"] = "myTax 填写指南",
        ["year"] = "FY {0}–{1:00} 财年 · {0} 年 7 月 1 日至 {2} 年 6 月 30 日",
        ["prepared"] = "生成于 {0}",
        ["footer"] = "KuTax · 报税准备资料 · 并非已提交的报税表 · 第 ",
        ["of"] = " 页，共 ",
        ["claim"] = "可申报的抵扣总额",
        ["claim.note"] = "这笔金额会从你的应税收入中扣除，并不是退税金额。实际退税取决于你的税率和已预扣的税款，由 myTax 计算。",
        ["enter.title"] = "在 myTax 中填写",
        ["col.section"] = "myTax 栏目", ["col.claimType"] = "Claim type（申报类型）", ["col.amount"] = "金额", ["col.claims"] = "笔数",
        ["col.category"] = "类别", ["col.date"] = "日期", ["col.describe"] = "在 myTax 中输入的描述（英文）", ["col.receipt"] = "收据",
        ["col.done"] = "完成", ["col.merchant"] = "商家", ["col.expense"] = "费用", ["col.workUse"] = "工作用途",
        ["col.reimbursed"] = "已报销", ["col.deductible"] = "可抵扣", ["col.status"] = "状态", ["col.why"] = "原因",
        ["col.section.total"] = "栏目合计",
        ["col.description"] = "说明", ["col.invoice"] = "收据总额", ["col.foreign"] = "外币",
        ["col.conditions"] = "条件", ["col.source"] = "ATO 来源", ["col.reviewed"] = "确认日期", ["col.note"] = "备注",
        ["col.file"] = "收据压缩包中的文件", ["col.original"] = "上传时的文件名", ["col.size"] = "大小（KB）",
        ["fx.rba"] = "澳洲储备银行汇率（估算）", ["fx.statement"] = "银行或信用卡账单",
        ["whole"] = "填写整数金额；每个栏目合计后只舍去一次零头（分）。",
        ["bycategory"] = "按类别",
        ["left.title"] = "未计入",
        ["left.none"] = "没有遗漏的项目。",
        ["left.advice"] = "{0} 笔费用（{1}）需要咨询或补充信息，已列在明细表中。",
        ["left.excluded"] = "{0} 笔费用（{1}）被标记为私人或已排除。",
        ["left.issues"] = "{0} 条收据备注，例如未分配的余额或缺少日期。",
        ["left.advice.one"] = "1 笔费用（{1}）需要咨询或补充信息，已列在明细表中。",
        ["left.excluded.one"] = "1 笔费用（{1}）被标记为私人或已排除。",
        ["left.issues.one"] = "1 条收据备注，例如未分配的余额或缺少日期。",
        ["disclaimer"] = "KuTax 根据你确认过的收据，以及管理员对照 ATO 资料核实过的规则计算金额。KuTax 不代你提交报税表，也不提供税务建议。如有疑问，请向 ATO 或注册税务代理确认。以英文版为准。",
        ["guide.intro"] = "在 myTax 中按本指南操作。每个栏目都列出了对应的收据，你可以逐笔输入或核对。myTax 是英文界面，因此栏目名称和要输入的描述保留英文。",
        ["guide.step1"] = "登录 myGov，打开 ATO，选择 Prepare tax return（准备报税表）。",
        ["guide.step2"] = "进入 Deductions（抵扣），依次打开下面的每个栏目。有些金额（例如工会会费）可能已预填，不要重复添加。",
        ["guide.step3"] = "按下表的描述和金额逐笔添加（如果 myTax 只要求一个合计，就填写栏目合计），并按提示选择 claim type。",
        ["guide.step4"] = "每录入一行就打个勾。请将本资料保存 5 年。",
        ["guide.section"] = "本栏目应填写合计：{0}",
        ["guide.claimType"] = "Claim type：{0}",
        ["guide.workUse"] = "{0}% 工作用途",
        ["guide.reimbursed"] = "扣除已报销 {0}",
        ["sheet.summary"] = "摘要", ["sheet.entries"] = "myTax 填写", ["sheet.left"] = "未申报", ["sheet.details"] = "全部费用",
        ["sheet.rules"] = "规则", ["sheet.evidence"] = "证据",
        ["sum.approved"] = "可申报的抵扣总额", ["sum.entry"] = "各栏目整数金额合计", ["sum.claims"] = "已批准的申报",
        ["sum.waiting"] = "已计算但未批准", ["sum.unassessed"] = "需要咨询或补充信息", ["sum.excluded"] = "已排除",
        ["sum.year"] = "财年", ["sum.prepared"] = "生成时间", ["sum.note"] = "你的备注",
    };
}
