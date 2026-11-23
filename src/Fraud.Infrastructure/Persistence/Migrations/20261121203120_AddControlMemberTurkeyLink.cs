using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fraud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControlMemberTurkeyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "turkey_linked",
                table: "control_members",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_control_members_turkey",
                table: "control_members",
                sql: "turkey_linked IS NULL OR \"group\" = 'G2'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_control_members_turkey",
                table: "control_members");

            migrationBuilder.DropColumn(
                name: "turkey_linked",
                table: "control_members");
        }
    }
}
