using Fraud.Domain.Archives;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Certificates;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Infrastructure.Persistence;

public sealed class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options)
{
    public DbSet<DomainName> Domains => Set<DomainName>();

    public DbSet<OfficialAction> OfficialActions => Set<OfficialAction>();

    public DbSet<CollectionLogEntry> CollectionLog => Set<CollectionLogEntry>();

    public DbSet<Certificate> Certificates => Set<Certificate>();

    public DbSet<CertificateDomain> CertificateDomains => Set<CertificateDomain>();

    public DbSet<ArchiveSnapshot> ArchiveSnapshots => Set<ArchiveSnapshot>();

    public DbSet<CtCandidate> CtCandidates => Set<CtCandidate>();

    public DbSet<CtLogState> CtLogStates => Set<CtLogState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Tablo ve sütun adları açıkça yazılır (snake_case). Sınıf adı değişse bile şema değişmez.
        modelBuilder.Entity<DomainName>(e =>
        {
            e.ToTable("domains");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Host).HasColumnName("host").HasMaxLength(253).IsRequired();
            e.Property(x => x.RegistrableDomain).HasColumnName("registrable_domain").HasMaxLength(253).IsRequired();
            e.Property(x => x.PublicSuffix).HasColumnName("public_suffix").HasMaxLength(253).IsRequired();
            e.Property(x => x.UnicodeHost).HasColumnName("unicode_host").HasMaxLength(1024).IsRequired();
            e.Property(x => x.FirstObservedAt).HasColumnName("first_observed_at").IsRequired();

            // Aynı ad iki kez kaydedilmez: yükleyicinin idempotent olmasının veritabanı tarafındaki güvencesi.
            e.HasIndex(x => x.Host).IsUnique().HasDatabaseName("ux_domains_host");
            e.HasIndex(x => x.RegistrableDomain).HasDatabaseName("ix_domains_registrable_domain");
        });

        modelBuilder.Entity<OfficialAction>(e =>
        {
            e.ToTable("official_actions", t =>
            {
                t.HasCheckConstraint("ck_official_actions_bulletin_number", "length(btrim(bulletin_number)) > 0");
                t.HasCheckConstraint("ck_official_actions_source_url", "source_url ~ '^https?://'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.DomainId).HasColumnName("domain_id").IsRequired();
            e.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.BulletinNumber).HasColumnName("bulletin_number").HasMaxLength(32).IsRequired();
            e.Property(x => x.DecisionDate).HasColumnName("decision_date").IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(2048).IsRequired();
            e.Property(x => x.ListedAs).HasColumnName("listed_as").HasMaxLength(2048).IsRequired();
            e.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_official_actions_domains");

            // Aynı kaynakta, aynı bültende, aynı alan adı için tek kayıt.
            e.HasIndex(x => new { x.Source, x.BulletinNumber, x.DomainId })
                .IsUnique()
                .HasDatabaseName("ux_official_actions_source_bulletin_domain");
            e.HasIndex(x => x.DecisionDate).HasDatabaseName("ix_official_actions_decision_date");
        });

        ConfigureCollectionLog(modelBuilder);
        ConfigureCertificates(modelBuilder);
        ConfigureArchiveSnapshots(modelBuilder);
        ConfigureCertificateTransparency(modelBuilder);
    }

    private static void ConfigureCollectionLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionLogEntry>(e =>
        {
            e.ToTable("collection_log", t =>
            {
                // Hata asla "kayıt yok" gibi görünemez: Boş sonuç yalnızca 2xx yanıtla ve sıfır kayıtla yazılır,
                // hata sonucu her zaman bir hata türü taşır. Uygulama katmanı atlansa bile veritabanı durdurur.
                // Dikkat: CHECK içinde NULL sonuç "geçti" sayılır. Bu yüzden http_status için IS NOT NULL açıkça yazılır.
                t.HasCheckConstraint("ck_collection_log_error_kind",
                    "(outcome = 'Error') = (error_kind <> 'None')");
                t.HasCheckConstraint("ck_collection_log_empty",
                    "outcome <> 'Empty' OR (item_count = 0 AND skipped_item_count = 0 AND http_status IS NOT NULL AND http_status BETWEEN 200 AND 299)");
                t.HasCheckConstraint("ck_collection_log_success", "outcome <> 'Success' OR item_count > 0");
                t.HasCheckConstraint("ck_collection_log_counts",
                    "attempts >= 0 AND item_count >= 0 AND skipped_item_count >= 0 AND new_item_count >= 0");
                t.HasCheckConstraint("ck_collection_log_times", "finished_at >= started_at");
                t.HasCheckConstraint("ck_collection_log_location", "length(btrim(measurement_location)) > 0");
                // Log aralığı: iki uç birlikte verilir, aralık ters olamaz.
                t.HasCheckConstraint("ck_collection_log_range",
                    "(range_start IS NULL AND range_end IS NULL) OR " +
                    "(range_start IS NOT NULL AND range_end IS NOT NULL AND range_start >= 0 AND range_end >= range_start)");
                t.HasCheckConstraint("ck_collection_log_ct_range_has_log", "range_start IS NULL OR ct_log_name IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.DomainId).HasColumnName("domain_id");
            e.Property(x => x.MeasurementLocation).HasColumnName("measurement_location").HasMaxLength(64).IsRequired();
            e.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            e.Property(x => x.FinishedAt).HasColumnName("finished_at").IsRequired();
            e.Property(x => x.HttpStatus).HasColumnName("http_status");
            e.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.ErrorKind).HasColumnName("error_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.ItemCount).HasColumnName("item_count").IsRequired();
            e.Property(x => x.SkippedItemCount).HasColumnName("skipped_item_count").IsRequired();
            e.Property(x => x.NewItemCount).HasColumnName("new_item_count").IsRequired();
            e.Property(x => x.Truncated).HasColumnName("truncated").IsRequired();
            e.Property(x => x.CtLogName).HasColumnName("ct_log_name").HasMaxLength(64);
            e.Property(x => x.RangeStart).HasColumnName("range_start");
            e.Property(x => x.RangeEnd).HasColumnName("range_end");

            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_collection_log_domains");

            // Önbellek sorgusu: "Bu kaynak bu alan adını en son ne zaman başarıyla sordu?"
            e.HasIndex(x => new { x.Source, x.DomainId, x.FinishedAt }).HasDatabaseName("ix_collection_log_source_domain_finished");
            // Kapsama sorgusu: "Bu logda hangi aralıklar hata verdi?"
            e.HasIndex(x => new { x.CtLogName, x.RangeStart })
                .HasFilter("ct_log_name IS NOT NULL")
                .HasDatabaseName("ix_collection_log_ct_log_range");
        });
    }

    private static void ConfigureCertificates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Certificate>(e =>
        {
            e.ToTable("certificates", t =>
            {
                t.HasCheckConstraint("ck_certificates_issuer_no_email", "position('@' in issuer_name) = 0");
                t.HasCheckConstraint("ck_certificates_validity", "not_after >= not_before");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.CrtShId).HasColumnName("crtsh_id").IsRequired();
            e.Property(x => x.SerialNumber).HasColumnName("serial_number").HasMaxLength(128).IsRequired();
            e.Property(x => x.IssuerCaId).HasColumnName("issuer_ca_id");
            e.Property(x => x.IssuerName).HasColumnName("issuer_name").HasMaxLength(1024).IsRequired();
            e.Property(x => x.CommonName).HasColumnName("common_name").HasMaxLength(253);
            e.Property(x => x.Names).HasColumnName("names").HasColumnType("text[]").IsRequired();
            e.Property(x => x.NotBefore).HasColumnName("not_before").IsRequired();
            e.Property(x => x.NotAfter).HasColumnName("not_after").IsRequired();
            e.Property(x => x.LoggedAt).HasColumnName("logged_at");
            e.Property(x => x.IsPrecertificate).HasColumnName("is_precertificate");
            e.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

            // Aynı sertifika iki kez gelirse yeni satır açılmaz.
            e.HasIndex(x => x.CrtShId).IsUnique().HasDatabaseName("ux_certificates_crtsh_id");
            e.HasIndex(x => x.NotBefore).HasDatabaseName("ix_certificates_not_before");
        });

        modelBuilder.Entity<CertificateDomain>(e =>
        {
            e.ToTable("certificate_domains");
            e.HasKey(x => new { x.CertificateId, x.DomainId }).HasName("pk_certificate_domains");
            e.Property(x => x.CertificateId).HasColumnName("certificate_id");
            e.Property(x => x.DomainId).HasColumnName("domain_id");
            e.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

            e.HasOne(x => x.Certificate)
                .WithMany()
                .HasForeignKey(x => x.CertificateId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_certificate_domains_certificates");
            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_certificate_domains_domains");
            e.HasIndex(x => x.DomainId).HasDatabaseName("ix_certificate_domains_domain_id");
        });
    }

    private static void ConfigureArchiveSnapshots(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArchiveSnapshot>(e =>
        {
            e.ToTable("archive_snapshots", t =>
            {
                t.HasCheckConstraint("ck_archive_snapshots_timestamp", "cdx_timestamp ~ '^[0-9]{14}$'");
                t.HasCheckConstraint("ck_archive_snapshots_no_query", "position('?' in original_url) = 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.DomainId).HasColumnName("domain_id").IsRequired();
            e.Property(x => x.CaptureKey).HasColumnName("capture_key").HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.Timestamp).HasColumnName("cdx_timestamp").HasMaxLength(14).IsFixedLength().IsRequired();
            e.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            e.Property(x => x.OriginalUrl).HasColumnName("original_url").HasMaxLength(2048).IsRequired();
            e.Property(x => x.QueryRemoved).HasColumnName("query_removed").IsRequired();
            e.Property(x => x.StatusCode).HasColumnName("status_code");
            e.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(255);
            e.Property(x => x.Digest).HasColumnName("digest").HasMaxLength(64);
            e.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_archive_snapshots_domains");

            // Aynı CDX satırı aynı alan adı için iki kez kaydedilmez.
            e.HasIndex(x => new { x.DomainId, x.CaptureKey }).IsUnique().HasDatabaseName("ux_archive_snapshots_domain_capture");
            e.HasIndex(x => new { x.DomainId, x.CapturedAt }).HasDatabaseName("ix_archive_snapshots_domain_captured_at");
        });
    }

    private static void ConfigureCertificateTransparency(ModelBuilder modelBuilder)
    {
        // Bu tablo SPK/USOM listesinde olmayan adlar içerir (Kural 4). API'ye, rapora ve loglara yalnızca sayısı çıkar.
        modelBuilder.Entity<CtCandidate>(e =>
        {
            e.ToTable("ct_candidates", t =>
            {
                t.HasCheckConstraint("ck_ct_candidates_issuer_no_email", "position('@' in issuer) = 0");
                t.HasCheckConstraint("ck_ct_candidates_index", "entry_index >= 0");
                t.HasCheckConstraint("ck_ct_candidates_names", "cardinality(san_names) BETWEEN 1 AND 1000");
                t.HasCheckConstraint("ck_ct_candidates_terms", "cardinality(matched_terms) >= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.LogName).HasColumnName("log_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.EntryIndex).HasColumnName("entry_index").IsRequired();
            e.Property(x => x.EntryType).HasColumnName("entry_type").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.LoggedAt).HasColumnName("logged_at").IsRequired();
            e.Property(x => x.NotBefore).HasColumnName("not_before");
            e.Property(x => x.NotAfter).HasColumnName("not_after");
            e.Property(x => x.Issuer).HasColumnName("issuer").HasMaxLength(1024).IsRequired();
            e.Property(x => x.SanNames).HasColumnName("san_names").HasColumnType("text[]").IsRequired();
            e.Property(x => x.MatchedTerms).HasColumnName("matched_terms").HasColumnType("text[]").IsRequired();
            e.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();

            // Aynı log kaydı iki kez okunursa yeni satır açılmaz.
            e.HasIndex(x => new { x.LogName, x.EntryIndex }).IsUnique().HasDatabaseName("ux_ct_candidates_log_index");
            e.HasIndex(x => x.NotBefore).HasDatabaseName("ix_ct_candidates_not_before");
        });

        modelBuilder.Entity<CtLogState>(e =>
        {
            e.ToTable("ct_log_state", t =>
            {
                t.HasCheckConstraint("ck_ct_log_state_next_index", "next_index >= 0 AND next_index % 256 = 0");
                t.HasCheckConstraint("ck_ct_log_state_frontier", "frontier IS NULL OR length(frontier) % 32 = 0");
                t.HasCheckConstraint("ck_ct_log_state_root", "length(pending_checkpoint_root) = 32");
                t.HasCheckConstraint("ck_ct_log_state_sizes", "pending_checkpoint_size <= latest_checkpoint_size");
                t.HasCheckConstraint("ck_ct_log_state_counts",
                    "entries_processed >= 0 AND entries_skipped >= 0 AND candidates_stored >= 0 AND consecutive_failures >= 0");
            });
            e.HasKey(x => x.LogName).HasName("pk_ct_log_state");
            e.Property(x => x.LogName).HasColumnName("log_name").HasMaxLength(64);
            e.Property(x => x.NextIndex).HasColumnName("next_index").IsRequired();
            e.Property(x => x.Frontier).HasColumnName("frontier");
            e.Property(x => x.PendingCheckpointSize).HasColumnName("pending_checkpoint_size").IsRequired();
            e.Property(x => x.PendingCheckpointRoot).HasColumnName("pending_checkpoint_root").IsRequired();
            e.Property(x => x.PendingCheckpointTimestamp).HasColumnName("pending_checkpoint_timestamp").IsRequired();
            e.Property(x => x.LatestCheckpointSize).HasColumnName("latest_checkpoint_size").IsRequired();
            e.Property(x => x.EntriesProcessed).HasColumnName("entries_processed").IsRequired();
            e.Property(x => x.EntriesSkipped).HasColumnName("entries_skipped").IsRequired();
            e.Property(x => x.CandidatesStored).HasColumnName("candidates_stored").IsRequired();
            e.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.Ignore(x => x.Coverage);
        });
    }
}
