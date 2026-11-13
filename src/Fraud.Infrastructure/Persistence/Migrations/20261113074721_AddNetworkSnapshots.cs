using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dns_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    domain_id = table.Column<long>(type: "bigint", nullable: false),
                    queried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    measured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resolver = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    transport = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    query_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rcode = table.Column<int>(type: "integer", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    answers = table.Column<string>(type: "jsonb", nullable: false),
                    negative_ttl = table.Column<long>(type: "bigint", nullable: true),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    block_marker = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dns_snapshots", x => x.id);
                    table.CheckConstraint("ck_dns_snapshots_answers_array", "jsonb_typeof(answers) = 'array'");
                    table.CheckConstraint("ck_dns_snapshots_block_marker", "NOT block_marker OR outcome = 'Success'");
                    table.CheckConstraint("ck_dns_snapshots_counts", "latency_ms >= 0 AND attempts >= 0");
                    table.CheckConstraint("ck_dns_snapshots_empty", "outcome <> 'Empty' OR (rcode IS NOT NULL AND rcode = 0 AND jsonb_array_length(answers) = 0)");
                    table.CheckConstraint("ck_dns_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                    table.CheckConstraint("ck_dns_snapshots_error_no_answers", "outcome <> 'Error' OR (jsonb_array_length(answers) = 0 AND NOT block_marker)");
                    table.CheckConstraint("ck_dns_snapshots_location", "length(btrim(location)) > 0");
                    table.CheckConstraint("ck_dns_snapshots_nxdomain", "outcome <> 'NxDomain' OR (rcode IS NOT NULL AND rcode = 3)");
                    table.CheckConstraint("ck_dns_snapshots_success", "outcome <> 'Success' OR (rcode IS NOT NULL AND rcode = 0 AND jsonb_array_length(answers) > 0)");
                    table.ForeignKey(
                        name: "fk_dns_snapshots_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ip_asn_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    queried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    measured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    asns = table.Column<long[]>(type: "bigint[]", nullable: false),
                    prefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    cdn_or_cloud = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ip_asn_snapshots", x => x.id);
                    table.CheckConstraint("ck_ip_asn_snapshots_data", "(outcome = 'Success') = (cardinality(asns) > 0 OR prefix IS NOT NULL)");
                    table.CheckConstraint("ck_ip_asn_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                    table.CheckConstraint("ck_ip_asn_snapshots_location", "length(btrim(location)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "port_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    queried_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    measured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    error_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    ports = table.Column<int[]>(type: "integer[]", nullable: false),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    cpes = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_port_snapshots", x => x.id);
                    table.CheckConstraint("ck_port_snapshots_error_kind", "(outcome = 'Error') = (error_kind <> 'None')");
                    table.CheckConstraint("ck_port_snapshots_location", "length(btrim(location)) > 0");
                    table.CheckConstraint("ck_port_snapshots_no_data_unless_success", "outcome = 'Success' OR (cardinality(ports) = 0 AND cardinality(tags) = 0 AND cardinality(cpes) = 0)");
                    table.CheckConstraint("ck_port_snapshots_port_range", "0 <= ALL (ports) AND 65535 >= ALL (ports)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_dns_snapshots_day_transport",
                table: "dns_snapshots",
                columns: new[] { "measured_on", "transport" });

            migrationBuilder.CreateIndex(
                name: "ux_dns_snapshots_daily",
                table: "dns_snapshots",
                columns: new[] { "domain_id", "location", "resolver", "transport", "query_type", "measured_on" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ip_asn_snapshots_queried_at",
                table: "ip_asn_snapshots",
                column: "queried_at");

            migrationBuilder.CreateIndex(
                name: "ix_port_snapshots_queried_at",
                table: "port_snapshots",
                column: "queried_at");

            // Elle eklendi: EF Core IPAddress sütununu modelde tekil indekse koyamıyor (FraudDbContext.ConfigureNetwork).
            // Aynı IP aynı UTC gününde bir kez kaydedilir; toplayıcıdaki ON CONFLICT (ip, measured_on) buna dayanır.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_ip_asn_snapshots_daily ON ip_asn_snapshots (ip, measured_on);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_port_snapshots_daily ON port_snapshots (ip, measured_on);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dns_snapshots");

            migrationBuilder.DropTable(
                name: "ip_asn_snapshots");

            migrationBuilder.DropTable(
                name: "port_snapshots");
        }
    }
}
