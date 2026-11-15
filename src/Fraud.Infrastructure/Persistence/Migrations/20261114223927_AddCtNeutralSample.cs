using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCtNeutralSample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ct_neutral_sample",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    registrable_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_log_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_entry_index = table.Column<long>(type: "bigint", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_log_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_entry_index = table.Column<long>(type: "bigint", nullable: false),
                    seen_count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ct_neutral_sample", x => x.id);
                    table.CheckConstraint("ck_ct_neutral_sample_counts", "seen_count >= 1 AND first_entry_index >= 0 AND last_entry_index >= 0");
                    table.CheckConstraint("ck_ct_neutral_sample_domain", "length(btrim(registrable_domain)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ct_neutral_sample_first_seen_at",
                table: "ct_neutral_sample",
                column: "first_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_ct_neutral_sample_domain",
                table: "ct_neutral_sample",
                column: "registrable_domain",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ct_neutral_sample");
        }
    }
}
