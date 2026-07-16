using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleCatalogFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "analytics");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "auth");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "calendar");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "chat");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "chat_ai");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "configuration");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "core_hr");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "documents");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "exceptions");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "expense");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "grievance");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "hr_docs");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "integrations");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "learning");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "leave");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "monitoring");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "notifications");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "org");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "payroll");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "performance");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "recruitment");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "reports");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "roles");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "skills");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "verification");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "work_management");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "workflow_engine");

            migrationBuilder.DeleteData(
                table: "module_catalog",
                keyColumn: "module_key",
                keyValue: "workforce");

            migrationBuilder.DropColumn(
                name: "full_license_price",
                table: "module_catalog");

            migrationBuilder.DropColumn(
                name: "maintenance_rate",
                table: "module_catalog");

            migrationBuilder.RenameColumn(
                name: "price_brackets",
                table: "module_catalog",
                newName: "storage_reference");

            migrationBuilder.AddColumn<string>(
                name: "ai_token_reference",
                table: "module_catalog",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_ai_enabled",
                table: "module_catalog",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_storage_consuming",
                table: "module_catalog",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pricing_reference",
                table: "module_catalog",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "module_catalog_price_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    old_pricing_reference = table.Column<string>(type: "jsonb", nullable: true),
                    new_pricing_reference = table.Column<string>(type: "jsonb", nullable: true),
                    old_storage_reference = table.Column<string>(type: "jsonb", nullable: true),
                    new_storage_reference = table.Column<string>(type: "jsonb", nullable: true),
                    old_ai_token_reference = table.Column<string>(type: "jsonb", nullable: true),
                    new_ai_token_reference = table.Column<string>(type: "jsonb", nullable: true),
                    old_pricing_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    new_pricing_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_catalog_price_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_module_catalog_price_history_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_features",
                columns: table => new
                {
                    feature_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    module_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_default_included = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_features", x => x.feature_key);
                    table.ForeignKey(
                        name: "fk_module_features_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_permission_ownership",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permission_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_default_permission = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_permission_ownership", x => new { x.module_key, x.permission_code });
                    table.ForeignKey(
                        name: "fk_module_permission_ownership_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_module_catalog_price_history_module_key",
                table: "module_catalog_price_history",
                column: "module_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_features_module_key",
                table: "module_features",
                column: "module_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_permission_ownership_permission_code",
                table: "module_permission_ownership",
                column: "permission_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_catalog_price_history");

            migrationBuilder.DropTable(
                name: "module_features");

            migrationBuilder.DropTable(
                name: "module_permission_ownership");

            migrationBuilder.DropColumn(
                name: "ai_token_reference",
                table: "module_catalog");

            migrationBuilder.DropColumn(
                name: "is_ai_enabled",
                table: "module_catalog");

            migrationBuilder.DropColumn(
                name: "is_storage_consuming",
                table: "module_catalog");

            migrationBuilder.DropColumn(
                name: "pricing_reference",
                table: "module_catalog");

            migrationBuilder.RenameColumn(
                name: "storage_reference",
                table: "module_catalog",
                newName: "price_brackets");

            migrationBuilder.AddColumn<decimal>(
                name: "full_license_price",
                table: "module_catalog",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "maintenance_rate",
                table: "module_catalog",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                values: new object[,]
                {
                    { "analytics", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Analytics & Reports", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "auth", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Auth & Security", "phase_1", "shared", "[]", "flat_rate", null },
                    { "calendar", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Calendar", "phase_1", "hr_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "chat", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Chat", "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "chat_ai", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Chat AI", "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "configuration", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Configuration", "phase_1", "shared", "[]", "flat_rate", null },
                    { "core_hr", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Core HR", "phase_1", "hr_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":4.0,\"annual_price\":40.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":3.0,\"annual_price\":30.0}]", "per_employee", null },
                    { "documents", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Documents", "phase_2", "worksync", "[]", "per_employee", null },
                    { "exceptions", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Exception Engine", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.5,\"annual_price\":25.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.5,\"annual_price\":15.0}]", "per_employee", null },
                    { "expense", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Expense Management", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "grievance", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Grievance Management", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "hr_docs", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "HR Documents", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "integrations", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Integrations", "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":-1,\"monthly_price\":50.0,\"annual_price\":500.0}]", "flat_rate", null },
                    { "learning", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Learning & Development", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "leave", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Leave Management", "phase_1", "hr_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.5,\"annual_price\":25.0}]", "per_employee", null },
                    { "monitoring", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Activity Monitoring", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":5.0,\"annual_price\":50.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":4.5,\"annual_price\":45.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":4.0,\"annual_price\":40.0}]", "per_employee", null },
                    { "notifications", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Notifications", "phase_1", "shared", "[]", "flat_rate", null },
                    { "org", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Org Structure", "phase_1", "shared", "[]", "flat_rate", null },
                    { "payroll", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Payroll", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "performance", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Performance Management", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "recruitment", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Recruitment", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "reports", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Reports", "phase_2", "worksync", "[]", "per_employee", null },
                    { "roles", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Roles & Permissions", "phase_1", "shared", "[]", "flat_rate", null },
                    { "skills", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Skills & Competencies", "phase_2", "hr_management", "[]", "per_employee", null },
                    { "verification", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Identity Verification", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.5,\"annual_price\":25.0}]", "per_employee", null },
                    { "work_management", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Work Management", "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":4.5,\"annual_price\":45.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":4.0,\"annual_price\":40.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":3.5,\"annual_price\":35.0}]", "per_employee", null },
                    { "workflow_engine", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Workflow Engine", "phase_1", "shared", "[]", "flat_rate", null },
                    { "workforce", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Workforce Analytics", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":2.5,\"annual_price\":25.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.0,\"annual_price\":20.0}]", "per_employee", null }
                });
        }
    }
}
