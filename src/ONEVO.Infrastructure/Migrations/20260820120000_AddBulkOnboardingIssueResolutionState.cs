using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260820120000_AddBulkOnboardingIssueResolutionState")]
    public partial class AddBulkOnboardingIssueResolutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "resolution_state_json",
                table: "bulk_onboarding_batches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolution_state_json",
                table: "bulk_onboarding_batches");

            migrationBuilder.DropColumn(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows");
        }
    }
}
