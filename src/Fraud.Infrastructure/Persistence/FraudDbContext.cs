using Fraud.Domain.DomainNames;
using Fraud.Domain.OfficialActions;
using Microsoft.EntityFrameworkCore;

namespace Fraud.Infrastructure.Persistence;

public sealed class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options)
{
    public DbSet<DomainName> Domains => Set<DomainName>();

    public DbSet<OfficialAction> OfficialActions => Set<OfficialAction>();

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
    }
}
