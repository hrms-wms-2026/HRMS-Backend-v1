using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingDraftReportsToEmployeeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reports_to_employee_id",
                table: "onboarding_drafts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reports_to_employee_id",
                table: "onboarding_drafts");

            migrationBuilder.DropColumn(
                name: "resolved_reports_to_employee_id",
                table: "bulk_onboarding_batch_rows");
        }
    }
}
