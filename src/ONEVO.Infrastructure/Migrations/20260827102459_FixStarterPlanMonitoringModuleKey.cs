using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixStarterPlanMonitoringModuleKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"org_structure\",\"core_hr\",\"leave\",\"calendar\",\"time_attendance\",\"monitoring\",\"discrepancy_engine\",\"identity_verification\",\"exception_engine\",\"productivity_analytics\",\"desktop_agent_gateway\",\"worksync_foundation\",\"projects\",\"objectives_milestones\",\"tasks\",\"boards\",\"planning_sprints\"]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"org_structure\",\"core_hr\",\"leave\",\"calendar\",\"time_attendance\",\"activity_monitoring\",\"discrepancy_engine\",\"identity_verification\",\"exception_engine\",\"productivity_analytics\",\"desktop_agent_gateway\",\"worksync_foundation\",\"projects\",\"objectives_milestones\",\"tasks\",\"boards\",\"planning_sprints\"]");
        }
    }
}
