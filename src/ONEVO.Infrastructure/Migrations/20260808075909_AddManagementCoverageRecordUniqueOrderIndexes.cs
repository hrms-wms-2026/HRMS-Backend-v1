using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagementCoverageRecordUniqueOrderIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_company_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Company' AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_department_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "covered_department_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Department' AND status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_active_position_order",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "covered_position_id", "owner_order" },
                unique: true,
                filter: "covered_target_type = 'Position' AND status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_management_coverage_records_active_company_order",
                table: "management_coverage_records");

            migrationBuilder.DropIndex(
                name: "ix_management_coverage_records_active_department_order",
                table: "management_coverage_records");

            migrationBuilder.DropIndex(
                name: "ix_management_coverage_records_active_position_order",
                table: "management_coverage_records");
        }
    }
}
