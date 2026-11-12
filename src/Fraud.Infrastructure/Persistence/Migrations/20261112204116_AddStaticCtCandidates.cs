using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaticCtCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ct_log_name",
                table: "collection_log",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "range_end",
                table: "collection_log",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "range_start",
                table: "collection_log",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ct_candidates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    log_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entry_index = table.Column<long>(type: "bigint", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    logged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issuer = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    san_names = table.Column<string[]>(type: "text[]", nullable: false),
                    matched_terms = table.Column<string[]>(type: "text[]", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ct_candidates", x => x.id);
                    table.CheckConstraint("ck_ct_candidates_index", "entry_index >= 0");
                    table.CheckConstraint("ck_ct_candidates_issuer_no_email", "position('@' in issuer) = 0");
                    table.CheckConstraint("ck_ct_candidates_names", "cardinality(san_names) BETWEEN 1 AND 1000");
                    table.CheckConstraint("ck_ct_candidates_terms", "cardinality(matched_terms) >= 1");
                });

            migrationBuilder.CreateTable(
                name: "ct_log_state",
                columns: table => new
                {
                    log_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_index = table.Column<long>(type: "bigint", nullable: false),
                    frontier = table.Column<byte[]>(type: "bytea", nullable: true),
                    pending_checkpoint_size = table.Column<long>(type: "bigint", nullable: false),
                    pending_checkpoint_root = table.Column<byte[]>(type: "bytea", nullable: false),
                    pending_checkpoint_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latest_checkpoint_size = table.Column<long>(type: "bigint", nullable: false),
                    entries_processed = table.Column<long>(type: "bigint", nullable: false),
                    entries_skipped = table.Column<long>(type: "bigint", nullable: false),
                    candidates_stored = table.Column<long>(type: "bigint", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ct_log_state", x => x.log_name);
                    table.CheckConstraint("ck_ct_log_state_counts", "entries_processed >= 0 AND entries_skipped >= 0 AND candidates_stored >= 0 AND consecutive_failures >= 0");
                    table.CheckConstraint("ck_ct_log_state_frontier", "frontier IS NULL OR length(frontier) % 32 = 0");
                    table.CheckConstraint("ck_ct_log_state_next_index", "next_index >= 0 AND next_index % 256 = 0");
                    table.CheckConstraint("ck_ct_log_state_root", "length(pending_checkpoint_root) = 32");
                    table.CheckConstraint("ck_ct_log_state_sizes", "pending_checkpoint_size <= latest_checkpoint_size");
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_log_ct_log_range",
                table: "collection_log",
                columns: new[] { "ct_log_name", "range_start" },
                filter: "ct_log_name IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_log_ct_range_has_log",
                table: "collection_log",
                sql: "range_start IS NULL OR ct_log_name IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_log_range",
                table: "collection_log",
                sql: "(range_start IS NULL AND range_end IS NULL) OR (range_start IS NOT NULL AND range_end IS NOT NULL AND range_start >= 0 AND range_end >= range_start)");

            migrationBuilder.CreateIndex(
                name: "ix_ct_candidates_not_before",
                table: "ct_candidates",
                column: "not_before");

            migrationBuilder.CreateIndex(
                name: "ux_ct_candidates_log_index",
                table: "ct_candidates",
                columns: new[] { "log_name", "entry_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ct_candidates");

            migrationBuilder.DropTable(
                name: "ct_log_state");

            migrationBuilder.DropIndex(
                name: "ix_collection_log_ct_log_range",
                table: "collection_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_log_ct_range_has_log",
                table: "collection_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_log_range",
                table: "collection_log");

            migrationBuilder.DropColumn(
                name: "ct_log_name",
                table: "collection_log");

            migrationBuilder.DropColumn(
                name: "range_end",
                table: "collection_log");

            migrationBuilder.DropColumn(
                name: "range_start",
                table: "collection_log");
        }
    }
}
