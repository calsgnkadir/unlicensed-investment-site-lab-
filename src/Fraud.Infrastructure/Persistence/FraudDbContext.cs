using Fraud.Domain.Archives;
using Fraud.Domain.CertificateTransparency;
using Fraud.Domain.Certificates;
using Fraud.Domain.Collection;
using Fraud.Domain.DomainNames;
using Fraud.Domain.Network;
using Fraud.Domain.OfficialActions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    public DbSet<CtNeutralSample> CtNeutralSamples => Set<CtNeutralSample>();

    public DbSet<Fraud.Domain.Control.ControlMember> ControlMembers => Set<Fraud.Domain.Control.ControlMember>();

    public DbSet<Fraud.Domain.Control.ControlBuild> ControlBuilds => Set<Fraud.Domain.Control.ControlBuild>();

    public DbSet<DnsSnapshot> DnsSnapshots => Set<DnsSnapshot>();

    public DbSet<IpAsnSnapshot> IpAsnSnapshots => Set<IpAsnSnapshot>();

    public DbSet<PortSnapshot> PortSnapshots => Set<PortSnapshot>();

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
        ConfigureNetwork(modelBuilder);
        ConfigureControl(modelBuilder);
    }

    private static void ConfigureControl(ModelBuilder modelBuilder)
    {
        // Kontrol grubu üyeleri. Listede olmayan adlara bağlıdır (Kural 4): Dışarıya yalnızca grup başına sayı çıkar.
        modelBuilder.Entity<Fraud.Domain.Control.ControlMember>(e =>
        {
            e.ToTable("control_members", t =>
            {
                t.HasCheckConstraint("ck_control_members_frame", "frame IS NULL OR \"group\" IN ('G3', 'G4b')");
                t.HasCheckConstraint("ck_control_members_seed", "length(btrim(seed)) > 0");
                t.HasCheckConstraint("ck_control_members_turkey", "turkey_linked IS NULL OR \"group\" = 'G2'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.DomainId).HasColumnName("domain_id").IsRequired();
            e.Property(x => x.Group).HasColumnName("group").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.Frame).HasColumnName("frame").HasConversion<string>().HasMaxLength(1);
            e.Property(x => x.AssignedAt).HasColumnName("assigned_at").IsRequired();
            e.Property(x => x.Week).HasColumnName("week").HasMaxLength(8);
            e.Property(x => x.Seed).HasColumnName("seed").HasMaxLength(128).IsRequired();
            e.Property(x => x.SourceListId).HasColumnName("source_list_id").HasMaxLength(64);
            e.Property(x => x.CommitmentId).HasColumnName("commitment_id").HasMaxLength(64);
            e.Property(x => x.FirstLoggedAt).HasColumnName("first_logged_at");
            e.Property(x => x.FirstCertificateAt).HasColumnName("first_certificate_at");
            e.Property(x => x.DictionarySubset).HasColumnName("dictionary_subset").HasMaxLength(16);
            e.Property(x => x.TurkeyLinked).HasColumnName("turkey_linked");

            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_control_members_domains");

            // Bir alan adı yalnızca bir grupta: Öncelik kuralı (E02) veritabanında da korunur.
            e.HasIndex(x => x.DomainId).IsUnique().HasDatabaseName("ux_control_members_domain");
            e.HasIndex(x => new { x.Group, x.Week }).HasDatabaseName("ix_control_members_group_week");
        });

        modelBuilder.Entity<Fraud.Domain.Control.ControlBuild>(e =>
        {
            e.ToTable("control_builds", t => t.HasCheckConstraint("ck_control_builds_selected", "selected >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Group).HasColumnName("group").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.WeekKey).HasColumnName("week_key").HasMaxLength(8).IsRequired();
            e.Property(x => x.BuiltAt).HasColumnName("built_at").IsRequired();
            e.Property(x => x.Seed).HasColumnName("seed").HasMaxLength(128).IsRequired();
            e.Property(x => x.SourceListId).HasColumnName("source_list_id").HasMaxLength(64);
            e.Property(x => x.CommitmentId).HasColumnName("commitment_id").HasMaxLength(64);
            e.Property(x => x.Selected).HasColumnName("selected").IsRequired();
            e.Property(x => x.Counts).HasColumnName("counts").HasColumnType("jsonb").IsRequired();

            // Aynı hafta ya da aynı tek seferlik grup ikinci kez kurulamaz (idempotentlik).
            e.HasIndex(x => new { x.Group, x.WeekKey }).IsUnique().HasDatabaseName("ux_control_builds_group_week");
        });
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

        // Tarafsız örneklem (Çerçeve N). Listede olmayan adlar içerir (Kural 4): Dışarıya yalnızca sayısı çıkar.
        modelBuilder.Entity<CtNeutralSample>(e =>
        {
            e.ToTable("ct_neutral_sample", t =>
            {
                t.HasCheckConstraint("ck_ct_neutral_sample_domain", "length(btrim(registrable_domain)) > 0");
                t.HasCheckConstraint("ck_ct_neutral_sample_counts", "seen_count >= 1 AND first_entry_index >= 0 AND last_entry_index >= 0");
                // Bilerek "last_seen_at >= first_seen_at" denetimi yok: Saat geri giderse (NTP düzeltmesi) bütün tur
                // yazılamaz ve akış takılırdı.
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.RegistrableDomain).HasColumnName("registrable_domain").HasMaxLength(253).IsRequired();
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
            e.Property(x => x.FirstLoggedAt).HasColumnName("first_logged_at").IsRequired();
            e.Property(x => x.FirstNotBefore).HasColumnName("first_not_before");
            e.Property(x => x.FirstLogName).HasColumnName("first_log_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.FirstEntryIndex).HasColumnName("first_entry_index").IsRequired();
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
            e.Property(x => x.LastLogName).HasColumnName("last_log_name").HasMaxLength(64).IsRequired();
            e.Property(x => x.LastEntryIndex).HasColumnName("last_entry_index").IsRequired();
            e.Property(x => x.SeenCount).HasColumnName("seen_count").IsRequired();

            // Alan adı başına tek satır: ilk görülme kalır, sonrakiler seen_count'u artırır.
            e.HasIndex(x => x.RegistrableDomain).IsUnique().HasDatabaseName("ux_ct_neutral_sample_domain");
            // Haftalık tabakalama "ilk görülme haftası"na göre yapılır (KONTROL_GRUBU_YONTEMI.md 4.5).
            e.HasIndex(x => x.FirstSeenAt).HasDatabaseName("ix_ct_neutral_sample_first_seen_at");
        });
    }

    private static void ConfigureNetwork(ModelBuilder modelBuilder)
    {
        // DNS ölçümleri. Ağ kayıtlarında zaman, konum, kaynak (çözücü ve yol) ve hata durumu zorunludur.
        // Dikkat: CHECK içinde NULL sonuç "geçti" sayılır; rcode için IS NOT NULL açıkça yazılır.
        modelBuilder.Entity<DnsSnapshot>(e =>
        {
            e.ToTable("dns_snapshots", t =>
            {
                t.HasCheckConstraint("ck_dns_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                t.HasCheckConstraint("ck_dns_snapshots_answers_array", "jsonb_typeof(answers) = 'array'");
                t.HasCheckConstraint("ck_dns_snapshots_success",
                    "outcome <> 'Success' OR (rcode IS NOT NULL AND rcode = 0 AND jsonb_array_length(answers) > 0)");
                t.HasCheckConstraint("ck_dns_snapshots_empty",
                    "outcome <> 'Empty' OR (rcode IS NOT NULL AND rcode = 0 AND jsonb_array_length(answers) = 0)");
                t.HasCheckConstraint("ck_dns_snapshots_nxdomain", "outcome <> 'NxDomain' OR (rcode IS NOT NULL AND rcode = 3)");
                // Hata hiçbir zaman cevap taşımaz; engel işareti yalnızca cevaplı sonuçta olur.
                t.HasCheckConstraint("ck_dns_snapshots_error_no_answers",
                    "outcome <> 'Error' OR (jsonb_array_length(answers) = 0 AND NOT block_marker)");
                t.HasCheckConstraint("ck_dns_snapshots_block_marker", "NOT block_marker OR outcome = 'Success'");
                t.HasCheckConstraint("ck_dns_snapshots_location", "length(btrim(location)) > 0");
                t.HasCheckConstraint("ck_dns_snapshots_counts", "latency_ms >= 0 AND attempts >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.DomainId).HasColumnName("domain_id").IsRequired();
            e.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();
            e.Property(x => x.MeasuredOn).HasColumnName("measured_on").IsRequired();
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(64).IsRequired();
            e.Property(x => x.Resolver).HasColumnName("resolver").HasMaxLength(32).IsRequired();
            e.Property(x => x.Transport).HasColumnName("transport").HasConversion<string>().HasMaxLength(8).IsRequired();
            e.Property(x => x.QueryType).HasColumnName("query_type").HasMaxLength(16).IsRequired();
            e.Property(x => x.Rcode).HasColumnName("rcode");
            e.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.ErrorKind).HasColumnName("error_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Answers)
                .HasColumnName("answers")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => DnsAnswerJson.Serialize(v),
                    v => DnsAnswerJson.Deserialize(v),
                    new ValueComparer<IReadOnlyList<DnsAnswer>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                        v => v.ToList()))
                .IsRequired();
            e.Property(x => x.NegativeTtl).HasColumnName("negative_ttl");
            e.Property(x => x.Truncated).HasColumnName("truncated").IsRequired();
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms").IsRequired();
            e.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
            e.Property(x => x.BlockMarker).HasColumnName("block_marker").IsRequired();

            e.HasOne(x => x.Domain)
                .WithMany()
                .HasForeignKey(x => x.DomainId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_dns_snapshots_domains");

            // Aynı gün, aynı konumdan, aynı çözücüye, aynı yoldan, aynı soru ikinci kez kaydedilmez (idempotentlik).
            e.HasIndex(x => new { x.DomainId, x.Location, x.Resolver, x.Transport, x.QueryType, x.MeasuredOn })
                .IsUnique()
                .HasDatabaseName("ux_dns_snapshots_daily");
            e.HasIndex(x => new { x.MeasuredOn, x.Transport }).HasDatabaseName("ix_dns_snapshots_day_transport");
        });

        // RIPEstat. Alan adı yok; yalnızca IP.
        modelBuilder.Entity<IpAsnSnapshot>(e =>
        {
            e.ToTable("ip_asn_snapshots", t =>
            {
                t.HasCheckConstraint("ck_ip_asn_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                t.HasCheckConstraint("ck_ip_asn_snapshots_data",
                    "(outcome = 'Success') = (cardinality(asns) > 0 OR prefix IS NOT NULL)");
                t.HasCheckConstraint("ck_ip_asn_snapshots_location", "length(btrim(location)) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Ip).HasColumnName("ip").HasColumnType("inet").IsRequired();
            e.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();
            e.Property(x => x.MeasuredOn).HasColumnName("measured_on").IsRequired();
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(64).IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.ErrorKind).HasColumnName("error_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.HttpStatus).HasColumnName("http_status");
            e.Property(x => x.Asns).HasColumnName("asns").HasColumnType("bigint[]").IsRequired();
            e.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(64);
            e.Property(x => x.CdnOrCloud).HasColumnName("cdn_or_cloud").HasMaxLength(32);

            // Tekil indeks ux_ip_asn_snapshots_daily (ip, measured_on) migration'da SQL ile kurulur: EF Core, IPAddress
            // karşılaştırılabilir olmadığı için onu modelde tekil indekse koymayı reddediyor. ON CONFLICT bu indekse dayanır.
            e.HasIndex(x => x.QueriedAt).HasDatabaseName("ix_ip_asn_snapshots_queried_at");
        });

        // InternetDB. vulns ve hostnames sütunu bilerek YOK.
        modelBuilder.Entity<PortSnapshot>(e =>
        {
            e.ToTable("port_snapshots", t =>
            {
                t.HasCheckConstraint("ck_port_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                t.HasCheckConstraint("ck_port_snapshots_no_data_unless_success",
                    "outcome = 'Success' OR (cardinality(ports) = 0 AND cardinality(tags) = 0 AND cardinality(cpes) = 0)");
                t.HasCheckConstraint("ck_port_snapshots_port_range", "0 <= ALL (ports) AND 65535 >= ALL (ports)");
                t.HasCheckConstraint("ck_port_snapshots_location", "length(btrim(location)) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Ip).HasColumnName("ip").HasColumnType("inet").IsRequired();
            e.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();
            e.Property(x => x.MeasuredOn).HasColumnName("measured_on").IsRequired();
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(64).IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.ErrorKind).HasColumnName("error_kind").HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.HttpStatus).HasColumnName("http_status");
            e.Property(x => x.Ports).HasColumnName("ports").HasColumnType("integer[]").IsRequired();
            e.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
            e.Property(x => x.Cpes).HasColumnName("cpes").HasColumnType("text[]").IsRequired();

            // Tekil indeks ux_port_snapshots_daily (ip, measured_on) migration'da SQL ile kurulur: EF Core, IPAddress
            // karşılaştırılabilir olmadığı için onu modelde tekil indekse koymayı reddediyor. ON CONFLICT bu indekse dayanır.
            e.HasIndex(x => x.QueriedAt).HasDatabaseName("ix_port_snapshots_queried_at");
        });
    }
}
