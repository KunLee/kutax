using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KuTax.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RulePacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxItemCost",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "MinAmount",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "WorkUseAbove",
                table: "TaxRules");

            migrationBuilder.AddColumn<string>(
                name: "DefinitionJson",
                table: "TaxRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TaxRules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PackId",
                table: "TaxRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RulePacks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PackJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RulePacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleSourceChecks",
                columns: table => new
                {
                    FinancialYearStart = table.Column<int>(type: "integer", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSourceChecks", x => x.FinancialYearStart);
                });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-Donations-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"minAmount\":2},\"checks\":[{\"when\":\"workUsePercent != 100 or reimbursement != 0\",\"status\":\"NeedsReview\",\"reason\":\"Gifts are claimed in full: use 100% and no reimbursement.\"},{\"when\":\"amount \\u003C minAmount\",\"status\":\"Excluded\",\"reason\":\"Gifts under ${minAmount} aren\\u0027t deductible.\"}],\"deductible\":\"amount\",\"tests\":[{\"name\":\"Gift in full\",\"inputs\":{\"amount\":50},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":50,\"parameters\":null},{\"name\":\"Below the minimum\",\"inputs\":{\"amount\":1.5},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":null,\"parameters\":{\"minAmount\":2}},{\"name\":\"Apportioned gift\",\"inputs\":{\"amount\":50,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[{\"field\":\"parameters.minAmount\",\"source\":\"instructions\",\"quote\":\"Did you make a gift or donation of $2 or more to an organisation that has a deductible gift recipient (DGR) status?\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-PhoneInternet-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Work-use share of a bill\",\"inputs\":{\"amount\":100,\"workUsePercent\":40},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":40,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ProfessionalMembership-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[{\"field\":\"paperReference\",\"source\":\"instructions\",\"quote\":\"union fees and subscriptions to trade, business or professional associations\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SelfEducation-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":\"K\",\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":\"K\",\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":\"K\",\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Claim type required\",\"inputs\":{\"amount\":100},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[{\"field\":\"claimTypes\",\"source\":\"instructions\",\"quote\":\"Select the code, K or I, that best describes your reason for incurring self-education expenses.\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SoftwareSubscriptions-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50},\"checks\":[{\"when\":\"amount \\u003E maxItemCost\",\"status\":\"Unsupported\",\"reason\":\"Items costing more than ${maxItemCost} decline in value over their effective life; KuTax doesn\\u0027t calculate that.\"},{\"when\":\"workUsePercent \\u003C= workUseAbove\",\"status\":\"Unsupported\",\"reason\":\"An immediate deduction needs more than {workUseAbove}% work use; otherwise the item declines in value, which KuTax doesn\\u0027t calculate.\"}],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"At the limit, mainly for work\",\"inputs\":{\"amount\":300,\"workUsePercent\":60},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":180,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Over the limit\",\"inputs\":{\"amount\":300.01},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Half work use isn\\u0027t mainly for work\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":50},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Subscription at 80%\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":23.99,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}}],\"evidence\":[{\"field\":\"parameters.maxItemCost\",\"source\":\"instructions\",\"quote\":\"an immediate deduction for the cost of depreciating assets costing $300 or less\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-TaxAffairs-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ToolsEquipment-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50},\"checks\":[{\"when\":\"amount \\u003E maxItemCost\",\"status\":\"Unsupported\",\"reason\":\"Items costing more than ${maxItemCost} decline in value over their effective life; KuTax doesn\\u0027t calculate that.\"},{\"when\":\"workUsePercent \\u003C= workUseAbove\",\"status\":\"Unsupported\",\"reason\":\"An immediate deduction needs more than {workUseAbove}% work use; otherwise the item declines in value, which KuTax doesn\\u0027t calculate.\"}],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"At the limit, mainly for work\",\"inputs\":{\"amount\":300,\"workUsePercent\":60},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":180,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Over the limit\",\"inputs\":{\"amount\":300.01},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Half work use isn\\u0027t mainly for work\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":50},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Subscription at 80%\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":23.99,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}}],\"evidence\":[{\"field\":\"parameters.maxItemCost\",\"source\":\"instructions\",\"quote\":\"an immediate deduction for the cost of depreciating assets costing $300 or less\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-UnionFees-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[{\"field\":\"paperReference\",\"source\":\"instructions\",\"quote\":\"union fees and subscriptions to trade, business or professional associations\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-WorkClothing-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":\"P\",\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":\"P\",\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":\"P\",\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Claim type required\",\"inputs\":{\"amount\":100},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-Donations-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"minAmount\":2},\"checks\":[{\"when\":\"workUsePercent != 100 or reimbursement != 0\",\"status\":\"NeedsReview\",\"reason\":\"Gifts are claimed in full: use 100% and no reimbursement.\"},{\"when\":\"amount \\u003C minAmount\",\"status\":\"Excluded\",\"reason\":\"Gifts under ${minAmount} aren\\u0027t deductible.\"}],\"deductible\":\"amount\",\"tests\":[{\"name\":\"Gift in full\",\"inputs\":{\"amount\":50},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":50,\"parameters\":null},{\"name\":\"Below the minimum\",\"inputs\":{\"amount\":1.5},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":null,\"parameters\":{\"minAmount\":2}},{\"name\":\"Apportioned gift\",\"inputs\":{\"amount\":50,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[{\"field\":\"parameters.minAmount\",\"source\":\"instructions\",\"quote\":\"Did you make a gift or donation of $2 or more to an organisation that has a deductible gift recipient (DGR) status?\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-PhoneInternet-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Work-use share of a bill\",\"inputs\":{\"amount\":100,\"workUsePercent\":40},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":40,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ProfessionalMembership-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[{\"field\":\"paperReference\",\"source\":\"instructions\",\"quote\":\"union fees and subscriptions to trade, business or professional associations\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SelfEducation-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":\"K\",\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":\"K\",\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":\"K\",\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Claim type required\",\"inputs\":{\"amount\":100},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[{\"field\":\"claimTypes\",\"source\":\"instructions\",\"quote\":\"Select the code, K or I, that best describes your reason for incurring self-education expenses.\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SoftwareSubscriptions-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50},\"checks\":[{\"when\":\"amount \\u003E maxItemCost\",\"status\":\"Unsupported\",\"reason\":\"Items costing more than ${maxItemCost} decline in value over their effective life; KuTax doesn\\u0027t calculate that.\"},{\"when\":\"workUsePercent \\u003C= workUseAbove\",\"status\":\"Unsupported\",\"reason\":\"An immediate deduction needs more than {workUseAbove}% work use; otherwise the item declines in value, which KuTax doesn\\u0027t calculate.\"}],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"At the limit, mainly for work\",\"inputs\":{\"amount\":300,\"workUsePercent\":60},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":180,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Over the limit\",\"inputs\":{\"amount\":300.01},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Half work use isn\\u0027t mainly for work\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":50},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Subscription at 80%\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":23.99,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}}],\"evidence\":[{\"field\":\"parameters.maxItemCost\",\"source\":\"instructions\",\"quote\":\"an immediate deduction for the cost of depreciating assets costing $300 or less\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-TaxAffairs-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ToolsEquipment-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50},\"checks\":[{\"when\":\"amount \\u003E maxItemCost\",\"status\":\"Unsupported\",\"reason\":\"Items costing more than ${maxItemCost} decline in value over their effective life; KuTax doesn\\u0027t calculate that.\"},{\"when\":\"workUsePercent \\u003C= workUseAbove\",\"status\":\"Unsupported\",\"reason\":\"An immediate deduction needs more than {workUseAbove}% work use; otherwise the item declines in value, which KuTax doesn\\u0027t calculate.\"}],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"At the limit, mainly for work\",\"inputs\":{\"amount\":300,\"workUsePercent\":60},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":180,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Over the limit\",\"inputs\":{\"amount\":300.01},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Half work use isn\\u0027t mainly for work\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":50},\"claimType\":null,\"expectStatus\":\"Unsupported\",\"expectDeductible\":null,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}},{\"name\":\"Subscription at 80%\",\"inputs\":{\"amount\":29.99,\"workUsePercent\":80},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":23.99,\"parameters\":{\"maxItemCost\":300,\"workUseAbove\":50}}],\"evidence\":[{\"field\":\"parameters.maxItemCost\",\"source\":\"instructions\",\"quote\":\"an immediate deduction for the cost of depreciating assets costing $300 or less\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-UnionFees-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":null,\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":null,\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null}],\"evidence\":[{\"field\":\"paperReference\",\"source\":\"instructions\",\"quote\":\"union fees and subscriptions to trade, business or professional associations\"}]}", true, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-WorkClothing-v1",
                columns: new[] { "DefinitionJson", "IsActive", "PackId" },
                values: new object[] { "{\"parameters\":{},\"checks\":[],\"deductible\":\"round2(amount * workUsePercent / 100) - reimbursement\",\"tests\":[{\"name\":\"Full work use\",\"inputs\":{\"amount\":440,\"workUsePercent\":100},\"claimType\":\"P\",\"expectStatus\":\"Candidate\",\"expectDeductible\":440,\"parameters\":null},{\"name\":\"Rounded work portion less reimbursement\",\"inputs\":{\"amount\":123.45,\"workUsePercent\":50,\"reimbursement\":1},\"claimType\":\"P\",\"expectStatus\":\"Candidate\",\"expectDeductible\":60.73,\"parameters\":null},{\"name\":\"No work use\",\"inputs\":{\"amount\":100,\"workUsePercent\":0},\"claimType\":\"P\",\"expectStatus\":\"Excluded\",\"expectDeductible\":0,\"parameters\":null},{\"name\":\"Claim type required\",\"inputs\":{\"amount\":100},\"claimType\":null,\"expectStatus\":\"NeedsReview\",\"expectDeductible\":null,\"parameters\":null}],\"evidence\":[]}", true, null });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRules_FinancialYearStart_Category",
                table: "TaxRules",
                columns: new[] { "FinancialYearStart", "Category" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_RulePacks_FinancialYearStart_Status",
                table: "RulePacks",
                columns: new[] { "FinancialYearStart", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RulePacks");

            migrationBuilder.DropTable(
                name: "RuleSourceChecks");

            migrationBuilder.DropIndex(
                name: "IX_TaxRules_FinancialYearStart_Category",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "DefinitionJson",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TaxRules");

            migrationBuilder.DropColumn(
                name: "PackId",
                table: "TaxRules");

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

            migrationBuilder.AddColumn<decimal>(
                name: "WorkUseAbove",
                table: "TaxRules",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-Donations-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, 2m, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-PhoneInternet-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ProfessionalMembership-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SelfEducation-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-SoftwareSubscriptions-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { 300m, null, 50m });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-TaxAffairs-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-ToolsEquipment-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { 300m, null, 50m });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-UnionFees-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2024-WorkClothing-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-Donations-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, 2m, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-PhoneInternet-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ProfessionalMembership-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SelfEducation-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-SoftwareSubscriptions-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { 300m, null, 50m });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-TaxAffairs-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-ToolsEquipment-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { 300m, null, 50m });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-UnionFees-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "TaxRules",
                keyColumn: "Version",
                keyValue: "AU-2025-WorkClothing-v1",
                columns: new[] { "MaxItemCost", "MinAmount", "WorkUseAbove" },
                values: new object[] { null, null, null });
        }
    }
}
