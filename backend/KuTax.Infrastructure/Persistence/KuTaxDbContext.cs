using KuTax.Infrastructure.Identity;
using KuTax.Core.Receipts;
using KuTax.Core.Claims;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KuTax.Infrastructure.Persistence;

public sealed class KuTaxDbContext(DbContextOptions<KuTaxDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    /// <summary>Sign-in and settings encryption keys when they live in the database (managed hosting); encrypted with a certificate.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<UserAvatar> Avatars => Set<UserAvatar>();
    public DbSet<ReceiptDocument> Documents => Set<ReceiptDocument>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportItem> ImportItems => Set<ImportItem>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<ExtractionAttempt> ExtractionAttempts => Set<ExtractionAttempt>();
    public DbSet<ReceiptTransaction> Transactions => Set<ReceiptTransaction>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PrivacyPreference> PrivacyPreferences => Set<PrivacyPreference>();
    public DbSet<ExpenseAllocation> Allocations => Set<ExpenseAllocation>();
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();
    public DbSet<TaxRuleReview> TaxRuleReviews => Set<TaxRuleReview>();
    public DbSet<RulePack> RulePacks => Set<RulePack>();
    public DbSet<RuleSourceCheck> RuleSourceChecks => Set<RuleSourceCheck>();
    public DbSet<TaxClaim> TaxClaims => Set<TaxClaim>();
    public DbSet<EvidenceLink> EvidenceLinks => Set<EvidenceLink>();
    public DbSet<ExportSnapshot> ExportSnapshots => Set<ExportSnapshot>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<EntryProgress> EntryProgress => Set<EntryProgress>();
    public DbSet<Processing.AiSettingsRecord> AiSettings => Set<Processing.AiSettingsRecord>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<KuTax.Core.Sharing.Delegation> Delegations => Set<KuTax.Core.Sharing.Delegation>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ReceiptDocument>().Property(d=>d.ScanStatus).HasDefaultValue("Pending");
        builder.Entity<ExchangeRate>(e=>{e.HasKey(x=>new{x.Currency,x.Date});e.Property(x=>x.Currency).HasMaxLength(3);e.Property(x=>x.UnitsPerAud).HasPrecision(18,6);});
        builder.Entity<ReceiptTransaction>(e=>{e.Property(x=>x.OriginalCurrency).HasMaxLength(3);e.Property(x=>x.OriginalAmount).HasPrecision(20,2);e.Property(x=>x.ConversionSource).HasMaxLength(20);e.Property(x=>x.ConversionRate).HasPrecision(18,6);e.Property(x=>x.SuggestedCategory).HasMaxLength(50);e.Property(x=>x.SkipReason).HasMaxLength(20);});
        builder.Entity<Processing.AiSettingsRecord>(e=>{e.HasKey(x=>x.Id);e.Property(x=>x.Id).ValueGeneratedNever();e.ToTable(t=>t.HasCheckConstraint("CK_AiSettings_Singleton","\"Id\" = 1"));e.Property(x=>x.Provider).HasMaxLength(20);e.Property(x=>x.Model).HasMaxLength(200);e.Property(x=>x.TextModel).HasMaxLength(200);e.Property(x=>x.Region).HasMaxLength(500);e.Property(x=>x.RetentionNotice).HasMaxLength(2000);e.Property(x=>x.ProtectedApiKey).HasMaxLength(4000);e.Property(x=>x.ApiKeyHint).HasMaxLength(10);e.Property(x=>x.UpdatedBy).HasMaxLength(450);});
        builder.Entity<ExportSnapshot>(e=>{e.HasKey(x=>x.Id);e.HasAlternateKey(x=>new{x.Id,x.OwnerId});e.HasIndex(x=>new{x.OwnerId,x.FinancialYearStart});e.Property(x=>x.SnapshotJson).HasColumnType("text");e.Property(x=>x.Sha256).HasMaxLength(64);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.OwnerId).OnDelete(DeleteBehavior.Restrict);});
        builder.Entity<ExportJob>(e=>{e.HasKey(x=>x.Id);e.HasIndex(x=>new{x.Status,x.AvailableAt});e.Property(x=>x.FilesJson).HasColumnType("jsonb");e.HasOne<ExportSnapshot>().WithOne().HasForeignKey<ExportJob>(x=>new{x.Id,x.OwnerId}).HasPrincipalKey<ExportSnapshot>(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict);});
        builder.Entity<EntryProgress>(e=>{e.HasKey(x=>new{x.SnapshotId,x.Section});e.HasOne<ExportSnapshot>().WithMany().HasForeignKey(x=>new{x.SnapshotId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict);});
        builder.Entity<TaxRule>(e => { e.HasKey(x=>x.Version); e.Property(x=>x.Version).HasMaxLength(100); e.Property(x=>x.DefinitionJson).HasColumnType("jsonb"); e.Property(x=>x.IsActive).HasDefaultValue(true); e.HasIndex(x=>new{x.FinancialYearStart,x.Category}).HasFilter("\"IsActive\"").IsUnique(); e.HasData(TaxRuleCatalog.Rules); });
        builder.Entity<RulePack>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.Status).HasMaxLength(20); e.Property(x=>x.Origin).HasMaxLength(20); e.Property(x=>x.PackJson).HasColumnType("jsonb"); e.Property(x=>x.ReportJson).HasColumnType("jsonb"); e.Property(x=>x.ErrorCode).HasMaxLength(60); e.Property(x=>x.Note).HasMaxLength(2000); e.HasIndex(x=>new{x.FinancialYearStart,x.Status}); });
        builder.Entity<RuleSourceCheck>(e => { e.HasKey(x=>x.FinancialYearStart); e.Property(x=>x.FinancialYearStart).ValueGeneratedNever(); e.Property(x=>x.Message).HasMaxLength(500); });
        builder.Entity<TaxRuleReview>(e => { e.HasKey(x=>x.RuleVersion); e.Property(x=>x.Note).HasMaxLength(2000); e.HasOne<TaxRule>().WithMany().HasForeignKey(x=>x.RuleVersion).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ExpenseAllocation>().HasAlternateKey(x=>new{x.Id,x.OwnerId});
        builder.Entity<ExpenseAllocation>().Property(x=>x.IsActive).HasDefaultValue(true);
        builder.Entity<EvidenceLink>(e => { e.HasKey(x=>new{x.AllocationId,x.DocumentId}); e.Property(x=>x.Note).HasMaxLength(500); e.HasOne<ExpenseAllocation>().WithMany().HasForeignKey(x=>new{x.AllocationId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); e.HasOne<ReceiptDocument>().WithMany().HasForeignKey(x=>new{x.DocumentId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<TaxClaim>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.AllocationId,x.AllocationVersion,x.TransactionVersion}).IsUnique(); e.Property(x=>x.DeductibleAmount).HasPrecision(18,2); e.Property(x=>x.EvidenceJson).HasColumnType("jsonb"); e.HasOne<ExpenseAllocation>().WithMany().HasForeignKey(x=>new{x.AllocationId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); e.HasOne<TaxRule>().WithMany().HasForeignKey(x=>x.RuleVersion).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ApplicationUser>().Property(user => user.DisplayName).HasMaxLength(100);
        builder.Entity<ApplicationUser>().Property(user => user.Occupation).HasMaxLength(100);
        builder.Entity<ApplicationUser>().Property(user => user.AdminNote).HasMaxLength(1000);
        builder.Entity<UserAvatar>(e => { e.HasKey(x => x.UserId); e.Property(x => x.ContentType).HasMaxLength(20); e.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserAvatar>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        // Existing accounts remain enabled when the migration is applied.
        builder.Entity<ApplicationUser>().Property(user => user.IsEnabled).HasDefaultValue(true);
        builder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(batch => batch.Id);
            entity.HasAlternateKey(batch => new { batch.Id, batch.OwnerId });
            entity.Property(batch => batch.Purpose).HasMaxLength(20).HasDefaultValue("Receipt");
            entity.Property(batch => batch.SourceKind).HasMaxLength(20);
            entity.Property(batch => batch.Status).HasMaxLength(20);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(batch => batch.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ReceiptDocument>(entity =>
        {
            entity.HasKey(document => document.Id);
            entity.HasAlternateKey(document => new { document.Id, document.OwnerId });
            entity.HasIndex(document => new { document.OwnerId, document.Sha256 }).IsUnique();
            entity.Property(document => document.Sha256).HasMaxLength(64);
            entity.Property(document => document.ObjectKey).HasMaxLength(160);
            entity.Property(document => document.OriginalFileName).HasMaxLength(200);
            entity.Property(document => document.RelativePath).HasMaxLength(512);
            entity.Property(document => document.ContentType).HasMaxLength(100);
            entity.Property(document => document.Status).HasMaxLength(30);
            entity.Property(document => document.Purpose).HasMaxLength(20).HasDefaultValue("Receipt");
            entity.Property(document => document.Version).IsConcurrencyToken();
            entity.HasOne<ImportBatch>().WithMany().HasForeignKey(document => new { document.ImportBatchId, document.OwnerId })
                .HasPrincipalKey(batch => new { batch.Id, batch.OwnerId }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ImportItem>(entity =>
        {
            entity.HasKey(item => new { item.BatchId, item.ClientId });
            entity.Property(item => item.RelativePath).HasMaxLength(512);
            entity.Property(item => item.Status).HasMaxLength(30);
            entity.Property(item => item.Message).HasMaxLength(200);
            entity.HasOne<ImportBatch>().WithMany().HasForeignKey(item => item.BatchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ReceiptDocument>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProcessingJob>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.DocumentId).IsUnique(); e.HasIndex(x=>new{x.Status,x.AvailableAt}); e.Property(x=>x.Status).HasMaxLength(30); e.Property(x=>x.ErrorCode).HasMaxLength(60); e.HasOne<ReceiptDocument>().WithMany().HasForeignKey(x=>new{x.DocumentId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ExtractionAttempt>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.CandidateJson).HasColumnType("jsonb"); e.Property(x=>x.Provider).HasMaxLength(50); e.Property(x=>x.Model).HasMaxLength(100); e.HasOne<ReceiptDocument>().WithMany().HasForeignKey(x=>new{x.DocumentId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ReceiptTransaction>(e => { e.HasKey(x=>x.Id); e.HasAlternateKey(x=>new{x.Id,x.OwnerId}); e.HasIndex(x=>x.DocumentId).IsUnique(); e.Property(x=>x.Merchant).HasMaxLength(200); e.Property(x=>x.Notes).HasMaxLength(2000); e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.Gst).HasPrecision(18,2); e.Property(x=>x.Version).IsConcurrencyToken(); e.HasOne<ReceiptDocument>().WithMany().HasForeignKey(x=>new{x.DocumentId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<AuditEvent>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.OwnerId,x.ResourceId}); e.Property(x=>x.Action).HasMaxLength(80); e.Property(x=>x.DetailsJson).HasColumnType("jsonb"); });
        builder.Entity<KuTax.Core.Sharing.Delegation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.DelegateId, x.Status });
            // One open invitation or active delegation per owner and address.
            e.HasIndex(x => new { x.OwnerId, x.Email }).IsUnique().HasFilter("\"Status\" IN ('Pending', 'Active')");
            // Deleting either account (only possible for accounts without records) ends the delegation with it.
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DelegateId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PrivacyPreference>(e => { e.HasKey(x=>x.OwnerId); e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.OwnerId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<ExpenseAllocation>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.Description).HasMaxLength(200); e.Property(x=>x.Category).HasMaxLength(50); e.Property(x=>x.ClaimType).HasMaxLength(1); e.Property(x=>x.Basis).HasMaxLength(2000); e.Property(x=>x.Amount).HasPrecision(18,2); e.Property(x=>x.WorkUsePercent).HasPrecision(5,2); e.Property(x=>x.WorkReimbursement).HasPrecision(18,2); e.Property(x=>x.Version).IsConcurrencyToken(); e.HasOne<ReceiptTransaction>().WithMany().HasForeignKey(x=>new{x.TransactionId,x.OwnerId}).HasPrincipalKey(x=>new{x.Id,x.OwnerId}).OnDelete(DeleteBehavior.Restrict); });
    }
}

public static class TaxRuleQueries
{
    /// <summary>Every rule version in the database: seeded built-ins plus versions from activated rule packs.</summary>
    public static async Task<TaxRuleSet> LoadRulesAsync(this KuTaxDbContext db,CancellationToken ct)=>new(await db.TaxRules.AsNoTracking().ToListAsync(ct));
}
