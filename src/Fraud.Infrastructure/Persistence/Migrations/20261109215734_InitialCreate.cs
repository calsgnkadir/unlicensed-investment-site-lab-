using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "domains",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    registrable_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    public_suffix = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    unicode_host = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domains", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "official_actions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    domain_id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bulletin_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    decision_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    listed_as = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_actions", x => x.id);
                    table.CheckConstraint("ck_official_actions_bulletin_number", "length(btrim(bulletin_number)) > 0");
                    table.CheckConstraint("ck_official_actions_source_url", "source_url ~ '^https?://'");
                    table.ForeignKey(
                        name: "fk_official_actions_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_domains_registrable_domain",
                table: "domains",
                column: "registrable_domain");

            migrationBuilder.CreateIndex(
                name: "ux_domains_host",
                table: "domains",
                column: "host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_official_actions_domain_id",
                table: "official_actions",
                column: "domain_id");

            migrationBuilder.CreateIndex(
                name: "ix_official_actions_decision_date",
                table: "official_actions",
                column: "decision_date");

            migrationBuilder.CreateIndex(
                name: "ux_official_actions_source_bulletin_domain",
                table: "official_actions",
                columns: new[] { "source", "bulletin_number", "domain_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "official_actions");

            migrationBuilder.DropTable(
                name: "domains");
        }
    }
}
