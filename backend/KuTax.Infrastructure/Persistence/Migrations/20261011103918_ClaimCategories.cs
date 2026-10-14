using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedCategory",
                table: "Transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ClaimTypes",
                table: "TaxRules",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "Declaration",
                table: "TaxRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "TaxRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxItemCost",
                table: "TaxRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinAmount",
                table: "TaxRules",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WholeAmountOnly",
                table: "TaxRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkUseAbove",
                table: "TaxRules",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimType",
                table: "Allocations",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ProfessionalMembership-v1",
                columns: new[] { "ClaimTypes", "Declaration", "Label", "MaxItemCost", "MinAmount", "WholeAmountOnly", "WorkUseAbove" },
                values: new object[] { new string[0], "I paid and incurred this periodic fee for my current employment in this financial year, kept written evidence, excluded private and reimbursed amounts, and checked it has no joining fee, levy or service for another year. I'll reconcile any payroll or myTax prefill so it isn't claimed twice.", "Professional memberships", null, null, false, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-UnionFees-v1",
                columns: new[] { "ClaimTypes", "Declaration", "Label", "MaxItemCost", "MinAmount", "WholeAmountOnly", "WorkUseAbove" },
                values: new object[] { new string[0], "I paid and incurred this periodic fee for my current employment in this financial year, kept written evidence, excluded private and reimbursed amounts, and checked it has no joining fee, levy or service for another year. I'll reconcile any payroll or myTax prefill so it isn't claimed twice.", "Union fees", null, null, false, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ProfessionalMembership-v1",
                columns: new[] { "ClaimTypes", "Declaration", "Label", "MaxItemCost", "MinAmount", "WholeAmountOnly", "WorkUseAbove" },
                values: new object[] { new string[0], "I paid and incurred this periodic fee for my current employment in this financial year, kept written evidence, excluded private and reimbursed amounts, and checked it has no joining fee, levy or service for another year. I'll reconcile any payroll or myTax prefill so it isn't claimed twice.", "Professional memberships", null, null, false, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-UnionFees-v1",
                columns: new[] { "ClaimTypes", "Declaration", "Label", "MaxItemCost", "MinAmount", "WholeAmountOnly", "WorkUseAbove" },
                values: new object[] { new string[0], "I paid and incurred this periodic fee for my current employment in this financial year, kept written evidence, excluded private and reimbursed amounts, and checked it has no joining fee, levy or service for another year. I'll reconcile any payroll or myTax prefill so it isn't claimed twice.", "Union fees", null, null, false, null });

            migrationBuilder.InsertData(
                table: "TaxRules",
                columns: new[] { "Version", "Category", "ClaimTypes", "Conditions", "Declaration", "FinancialYearStart", "Label", "MappingUrl", "MaxItemCost", "MinAmount", "PaperReference", "Section", "SourceSha256", "SourceUrl", "WholeAmountOnly", "WorkUseAbove" },
                values: new object[,]
                {
                    { "AU-2024-Donations-v1", "Donations", new string[0], "Gifts of money of $2 or more to an organisation with deductible gift recipient (DGR) status, where you received nothing in return (for example raffle tickets, dinners or merchandise). Gifts of property or shares, fundraising-event contributions and political donations are not calculated.", "This was a gift of $2 or more to an organisation that is a deductible gift recipient (DGR) — the receipt or ABN Lookup confirms it — and I received nothing in return.", 2024, "Gifts and donations", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", null, 2m, "D9 J", "Gifts or donations", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/gifts-and-donations", true, null },
                    { "AU-2024-PhoneInternet-v1", "PhoneInternet", new string[0], "Work-related portion of phone, mobile data and internet bills, worked out from your records (for example a representative 4-week diary and itemised bills). Not claimable if you use the fixed-rate method for working-from-home expenses, which already includes phone and internet usage. Handsets and devices are depreciating assets, not bills.", "This is a phone or internet bill. My work-use percentage comes from my records, such as a 4-week diary and itemised bills. I'm not using the working-from-home fixed-rate method this year, and private and reimbursed amounts are excluded.", 2024, "Phone and internet", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", null, null, "D5 E", "Other work-related expenses", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/mobile-phone-mobile-internet-and-other-devices", false, null },
                    { "AU-2024-SelfEducation-v1", "SelfEducation", new[] { "K", "I" }, "Course fees, textbooks, stationery and student fees for study with a sufficient connection to your employment when the cost was incurred: it maintains or improves skills or knowledge your current job requires (claim type K), or is likely to increase income from your current job (I). Not claimable: study while not employed, study only generally related to your job or to get a new job, Commonwealth supported place fees paid upfront or with HECS-HELP, HELP/SFSS/SSL/AASL/VSL repayments, and reimbursed amounts. FEE-HELP and VET Student Loan tuition can qualify. Travel, car and decline-in-value self-education costs are not calculated.", "I was employed when I paid this. The study maintains or improves skills or knowledge my current job requires, or is likely to increase my income from it — not only generally related, and not to get a new job. It isn't a Commonwealth supported place fee (paid upfront or with HECS-HELP) or a HELP repayment, and private and reimbursed amounts are excluded.", 2024, "Self-education", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", null, null, "D4 D", "Work-related self-education expenses", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/education-training-and-seminars/self-education-expenses", false, null },
                    { "AU-2024-SoftwareSubscriptions-v1", "SoftwareSubscriptions", new string[0], "Work-related portion of software, apps, subscriptions, reference books, technical journals and trade magazines costing $300 or less each, used mainly (more than 50%) for work, and not part of a set costing more than $300 together. Dearer items decline in value over their effective life and are not calculated.", "Each item cost $300 or less and isn't part of a set costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.", 2024, "Software and subscriptions", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", 300m, null, "D5 E", "Other work-related expenses", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/computers-laptops-and-software", false, 50m },
                    { "AU-2024-TaxAffairs-v1", "TaxAffairs", new string[0], "Fees for preparing and lodging your return through a registered tax agent, tax advice from a recognised tax adviser, and tax reference material or software (apportioned if also used for other purposes). Penalties, advice from someone who isn't a recognised tax adviser, and financial advice not about your tax affairs are not claimable. Litigation costs (label L) are not calculated.", "This cost was for managing my own tax affairs — a registered tax agent, a recognised tax adviser, or tax reference material or software — and is not a penalty or general financial advice. Other uses and reimbursed amounts are excluded.", 2024, "Managing tax affairs", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", null, null, "D10 M", "Cost of managing tax affairs", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/cost-of-managing-tax-affairs", false, null },
                    { "AU-2024-ToolsEquipment-v1", "ToolsEquipment", new string[0], "Immediate deduction for the work-related portion of a tool, device or piece of equipment that cost $300 or less, used mainly (more than 50%) for work, and not part of a set or a number of identical items costing more than $300 together. Items over $300 decline in value over their effective life and are not calculated. Not claimable when your employer supplied or reimbursed the item.", "Each item cost $300 or less, isn't part of a set or identical items costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.", 2024, "Tools and equipment", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", 300m, null, "D5 E", "Other work-related expenses", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/tools-and-equipment-to-perform-your-work", false, 50m },
                    { "AU-2024-WorkClothing-v1", "WorkClothing", new[] { "C", "N", "S", "P" }, "Buying, renting, repairing or dry-cleaning clothing you wear for work that is protective (P), occupation-specific (S), a distinctive compulsory uniform (C) or a non-compulsory uniform your employer registered (N). Plain clothing, such as black trousers, white shirts or suits, is not claimable even when required. Use the code for the main type at this label. Laundry claimed at a per-load rate is not calculated.", "This is protective clothing, occupation-specific clothing, a distinctive compulsory uniform, or a registered non-compulsory uniform that I wear for work — not plain or everyday clothing. Private and reimbursed amounts are excluded.", 2024, "Work clothing", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2025&dl=Y", null, null, "D3 C", "Work-related clothing, laundry and dry-cleaning expenses", "BD20CEF25336E2126B9C17625A13E30B158E229210761E19AEE32F2CDBA6BAC9", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/clothes-and-items-you-wear-at-work/clothing-laundry-and-dry-cleaning-expenses", false, null },
                    { "AU-2025-Donations-v1", "Donations", new string[0], "Gifts of money of $2 or more to an organisation with deductible gift recipient (DGR) status, where you received nothing in return (for example raffle tickets, dinners or merchandise). Gifts of property or shares, fundraising-event contributions and political donations are not calculated.", "This was a gift of $2 or more to an organisation that is a deductible gift recipient (DGR) — the receipt or ABN Lookup confirms it — and I received nothing in return.", 2025, "Gifts and donations", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", null, 2m, "D9 J", "Gifts or donations", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/gifts-and-donations", true, null },
                    { "AU-2025-PhoneInternet-v1", "PhoneInternet", new string[0], "Work-related portion of phone, mobile data and internet bills, worked out from your records (for example a representative 4-week diary and itemised bills). Not claimable if you use the fixed-rate method for working-from-home expenses, which already includes phone and internet usage. Handsets and devices are depreciating assets, not bills.", "This is a phone or internet bill. My work-use percentage comes from my records, such as a 4-week diary and itemised bills. I'm not using the working-from-home fixed-rate method this year, and private and reimbursed amounts are excluded.", 2025, "Phone and internet", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", null, null, "D5 E", "Other work-related expenses", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/mobile-phone-mobile-internet-and-other-devices", false, null },
                    { "AU-2025-SelfEducation-v1", "SelfEducation", new[] { "K", "I" }, "Course fees, textbooks, stationery and student fees for study with a sufficient connection to your employment when the cost was incurred: it maintains or improves skills or knowledge your current job requires (claim type K), or is likely to increase income from your current job (I). Not claimable: study while not employed, study only generally related to your job or to get a new job, Commonwealth supported place fees paid upfront or with HECS-HELP, HELP/SFSS/SSL/AASL/VSL repayments, and reimbursed amounts. FEE-HELP and VET Student Loan tuition can qualify. Travel, car and decline-in-value self-education costs are not calculated.", "I was employed when I paid this. The study maintains or improves skills or knowledge my current job requires, or is likely to increase my income from it — not only generally related, and not to get a new job. It isn't a Commonwealth supported place fee (paid upfront or with HECS-HELP) or a HELP repayment, and private and reimbursed amounts are excluded.", 2025, "Self-education", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", null, null, "D4 D", "Work-related self-education expenses", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/education-training-and-seminars/self-education-expenses", false, null },
                    { "AU-2025-SoftwareSubscriptions-v1", "SoftwareSubscriptions", new string[0], "Work-related portion of software, apps, subscriptions, reference books, technical journals and trade magazines costing $300 or less each, used mainly (more than 50%) for work, and not part of a set costing more than $300 together. Dearer items decline in value over their effective life and are not calculated.", "Each item cost $300 or less and isn't part of a set costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.", 2025, "Software and subscriptions", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", 300m, null, "D5 E", "Other work-related expenses", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/computers-laptops-and-software", false, 50m },
                    { "AU-2025-TaxAffairs-v1", "TaxAffairs", new string[0], "Fees for preparing and lodging your return through a registered tax agent, tax advice from a recognised tax adviser, and tax reference material or software (apportioned if also used for other purposes). Penalties, advice from someone who isn't a recognised tax adviser, and financial advice not about your tax affairs are not claimable. Litigation costs (label L) are not calculated.", "This cost was for managing my own tax affairs — a registered tax agent, a recognised tax adviser, or tax reference material or software — and is not a penalty or general financial advice. Other uses and reimbursed amounts are excluded.", 2025, "Managing tax affairs", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", null, null, "D10 M", "Cost of managing tax affairs", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/cost-of-managing-tax-affairs", false, null },
                    { "AU-2025-ToolsEquipment-v1", "ToolsEquipment", new string[0], "Immediate deduction for the work-related portion of a tool, device or piece of equipment that cost $300 or less, used mainly (more than 50%) for work, and not part of a set or a number of identical items costing more than $300 together. Items over $300 decline in value over their effective life and are not calculated. Not claimable when your employer supplied or reimbursed the item.", "Each item cost $300 or less, isn't part of a set or identical items costing more than $300 together, and I use it more than half the time for my current job. The work-use percentage is from my records; private and reimbursed amounts are excluded.", 2025, "Tools and equipment", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", 300m, null, "D5 E", "Other work-related expenses", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/tools-computers-and-items-you-use-for-work/tools-and-equipment-to-perform-your-work", false, 50m },
                    { "AU-2025-WorkClothing-v1", "WorkClothing", new[] { "C", "N", "S", "P" }, "Buying, renting, repairing or dry-cleaning clothing you wear for work that is protective (P), occupation-specific (S), a distinctive compulsory uniform (C) or a non-compulsory uniform your employer registered (N). Plain clothing, such as black trousers, white shirts or suits, is not claimable even when required. Use the code for the main type at this label. Laundry claimed at a per-load rate is not calculated.", "This is protective clothing, occupation-specific clothing, a distinctive compulsory uniform, or a registered non-compulsory uniform that I wear for work — not plain or everyday clothing. Private and reimbursed amounts are excluded.", 2025, "Work clothing", "https://iorder.com.au/publication/Download.aspx?ProdID=71050-6.2026&dl=Y", null, null, "D3 C", "Work-related clothing, laundry and dry-cleaning expenses", "07C64E4A98934DDD09D5F046AF901A35CB5085BC14F6B0AFF22FA4B5C7E041C3", "https://www.ato.gov.au/individuals-and-families/income-deductions-offsets-and-records/deductions-you-can-claim/work-related-deductions/clothes-and-items-you-wear-at-work/clothing-laundry-and-dry-cleaning-expenses", false, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-Donations-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-PhoneInternet-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SelfEducation-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SoftwareSubscriptions-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-TaxAffairs-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ToolsEquipment-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-WorkClothing-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-Donations-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-PhoneInternet-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SelfEducation-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SoftwareSubscriptions-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-TaxAffairs-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ToolsEquipment-v1");

            migrationBuilder.DeleteData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-WorkClothing-v1");

            migrationBuilder.DropColumn(
                name: "SuggestedCategory",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ClaimTypes",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "Declaration",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "MaxItemCost",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "MinAmount",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "WholeAmountOnly",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "WorkUseAbove",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "ClaimType",
                table: "Allocations");
        }
    }
}
