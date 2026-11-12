using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archive_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    domain_id = table.Column<long>(type: "bigint", nullable: false),
                    capture_key = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    cdx_timestamp = table.Column<string>(type: "character(14)", fixedLength: true, maxLength: 14, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    query_removed = table.Column<bool>(type: "boolean", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archive_snapshots", x => x.id);
                    table.CheckConstraint("ck_archive_snapshots_no_query", "position('?' in original_url) = 0");
                    table.CheckConstraint("ck_archive_snapshots_timestamp", "cdx_timestamp ~ '^[0-9]{14}$'");
                    table.ForeignKey(
                        name: "fk_archive_snapshots_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    crtsh_id = table.Column<long>(type: "bigint", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    issuer_ca_id = table.Column<int>(type: "integer", nullable: true),
                    issuer_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    common_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    names = table.Column<string[]>(type: "text[]", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_precertificate = table.Column<bool>(type: "boolean", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificates", x => x.id);
                    table.CheckConstraint("ck_certificates_issuer_no_email", "position('@' in issuer_name) = 0");
                    table.CheckConstraint("ck_certificates_validity", "not_after >= not_before");
                });

            migrationBuilder.CreateTable(
                name: "collection_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    domain_id = table.Column<long>(type: "bigint", nullable: true),
                    measurement_location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_item_count = table.Column<int>(type: "integer", nullable: false),
                    new_item_count = table.Column<int>(type: "integer", nullable: false),
                    truncated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_log", x => x.id);
                    table.CheckConstraint("ck_collection_log_counts", "attempts >= 0 AND item_count >= 0 AND skipped_item_count >= 0 AND new_item_count >= 0");
                    table.CheckConstraint("ck_collection_log_empty", "outcome <> 'Empty' OR (item_count = 0 AND skipped_item_count = 0 AND http_status IS NOT NULL AND http_status BETWEEN 200 AND 299)");
                    table.CheckConstraint("ck_collection_log_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                    table.CheckConstraint("ck_collection_log_location", "length(btrim(measurement_location)) > 0");
                    table.CheckConstraint("ck_collection_log_success", "outcome <> 'Success' OR item_count > 0");
                    table.CheckConstraint("ck_collection_log_times", "finished_at >= started_at");
                    table.ForeignKey(
                        name: "fk_collection_log_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "certificate_domains",
                columns: table => new
                {
                    certificate_id = table.Column<long>(type: "bigint", nullable: false),
                    domain_id = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_domains", x => new { x.certificate_id, x.domain_id });
                    table.ForeignKey(
                        name: "fk_certificate_domains_certificates",
                        column: x => x.certificate_id,
                        principalTable: "certificates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_certificate_domains_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_snapshots_domain_captured_at",
                table: "archive_snapshots",
                columns: new[] { "domain_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ux_archive_snapshots_domain_capture",
                table: "archive_snapshots",
                columns: new[] { "domain_id", "capture_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificate_domains_domain_id",
                table: "certificate_domains",
                column: "domain_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_not_before",
                table: "certificates",
                column: "not_before");

            migrationBuilder.CreateIndex(
                name: "ux_certificates_crtsh_id",
                table: "certificates",
                column: "crtsh_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_log_domain_id",
                table: "collection_log",
                column: "domain_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_log_source_domain_finished",
                table: "collection_log",
                columns: new[] { "source", "domain_id", "finished_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_snapshots");

            migrationBuilder.DropTable(
                name: "certificate_domains");

            migrationBuilder.DropTable(
                name: "collection_log");

            migrationBuilder.DropTable(
                name: "certificates");
        }
    }
}
