using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControlMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "control_builds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    group = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    week_key = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    commitment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    selected = table.Column<int>(type: "integer", nullable: false),
                    counts = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_builds", x => x.id);
                    table.CheckConstraint("ck_control_builds_selected", "selected >= 0");
                });

            migrationBuilder.CreateTable(
                name: "control_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    domain_id = table.Column<long>(type: "bigint", nullable: false),
                    group = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    frame = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    week = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    seed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    commitment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    first_logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_certificate_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dictionary_subset = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_control_members", x => x.id);
                    table.CheckConstraint("ck_control_members_frame", "frame IS NULL OR \"group\" IN ('G3', 'G4b')");
                    table.CheckConstraint("ck_control_members_seed", "length(btrim(seed)) > 0");
                    table.ForeignKey(
                        name: "fk_control_members_domains",
                        column: x => x.domain_id,
                        principalTable: "domains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_control_builds_group_week",
                table: "control_builds",
                columns: new[] { "group", "week_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_control_members_group_week",
                table: "control_members",
                columns: new[] { "group", "week" });

            migrationBuilder.CreateIndex(
                name: "ux_control_members_domain",
                table: "control_members",
                column: "domain_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "control_builds");

            migrationBuilder.DropTable(
                name: "control_members");
        }
    }
}
