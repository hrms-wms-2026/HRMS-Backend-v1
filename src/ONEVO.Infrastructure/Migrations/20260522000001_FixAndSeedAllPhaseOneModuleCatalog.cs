using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260522000001_FixAndSeedAllPhaseOneModuleCatalog")]
    public partial class FixAndSeedAllPhaseOneModuleCatalog : Migration
    {
        // This migration has no .Designer.cs (no target model snapshot), so every data
        // operation must carry explicit column types for EF to generate SQL.
        private static readonly string[] ModuleCatalogColumnTypes =
        [
            "character varying(100)",   // module_key
            "timestamp with time zone", // created_at
            "numeric(12,2)",            // full_license_price
            "boolean",                  // is_active
            "numeric(5,2)",             // maintenance_rate
            "character varying(150)",   // name
            "character varying(30)",    // phase
            "character varying(50)",    // pillar
            "jsonb",                    // price_brackets
            "character varying(30)",    // pricing_unit
            "timestamp with time zone"  // updated_at
        ];

        private const string ModuleKeyType = "character varying(100)";
        private const string PillarType = "character varying(50)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Fix 3 existing rows: wrong pillar values ──────────────────────────
            UpdateModulePillar(migrationBuilder, "chat_ai", "worksync");
            UpdateModulePillar(migrationBuilder, "core_hr", "hr_management");
            UpdateModulePillar(migrationBuilder, "work_management", "worksync");

            // ── Insert Foundation modules (not sellable — price_brackets = []) ───
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "auth",           new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Auth & Security",      "phase_1", "shared", "[]", "flat_rate", null },
                    { "configuration",  new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Configuration",        "phase_1", "shared", "[]", "flat_rate", null },
                    { "roles",          new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Roles & Permissions",  "phase_1", "shared", "[]", "flat_rate", null },
                    { "notifications",  new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Notifications",        "phase_1", "shared", "[]", "flat_rate", null },
                    { "org",            new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Org Structure",        "phase_1", "shared", "[]", "flat_rate", null },
                    { "workflow_engine",new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Workflow Engine",      "phase_1", "shared", "[]", "flat_rate", null },
                });

            // ── Insert HR Core modules ────────────────────────────────────────────
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "leave",    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Leave Management", "phase_1", "hr_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.5,\"annual_price\":25.0}]", "per_employee", null },
                    { "calendar", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Calendar",         "phase_1", "hr_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                });

            // ── Insert Intelligence (Workforce Intelligence) modules ──────────────
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "monitoring",   new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Activity Monitoring",   "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":5.0,\"annual_price\":50.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":4.5,\"annual_price\":45.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":4.0,\"annual_price\":40.0}]", "per_employee", null },
                    { "workforce",    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Workforce Analytics",   "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":2.5,\"annual_price\":25.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.0,\"annual_price\":20.0}]", "per_employee", null },
                    { "verification", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Identity Verification", "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.0,\"annual_price\":30.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":2.5,\"annual_price\":25.0}]", "per_employee", null },
                    { "exceptions",   new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Exception Engine",      "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.5,\"annual_price\":25.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.5,\"annual_price\":15.0}]", "per_employee", null },
                    { "analytics",    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Analytics & Reports",   "phase_1", "workforce_intelligence", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                });

            // ── Insert WorkSync modules ───────────────────────────────────────────
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "chat",         new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Chat",         "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "integrations", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Integrations", "phase_1", "worksync", "[{\"min_employees\":0,\"max_employees\":-1,\"monthly_price\":50.0,\"annual_price\":500.0}]",                                                                                                                                                                        "flat_rate",   null },
                });

            // ── Insert Phase 2 modules (is_active = false, no price brackets) ────
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "payroll",      new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Payroll",                   "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "performance",  new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Performance Management",     "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "skills",       new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Skills & Competencies",      "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "learning",     new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Learning & Development",     "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "recruitment",  new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Recruitment",                "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "hr_docs",      new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "HR Documents",               "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "grievance",    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Grievance Management",       "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "expense",      new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Expense Management",         "phase_2", "hr_management",          "[]", "per_employee", null },
                    { "documents",    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Documents",                  "phase_2", "worksync",               "[]", "per_employee", null },
                    { "reports",      new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, null, "Reports",                    "phase_2", "worksync",               "[]", "per_employee", null },
                });

            // ── Update seeded subscription plan to include workflow_engine ────────
            UpdatePlanModules(
                migrationBuilder,
                "[\"auth\",\"configuration\",\"roles\",\"notifications\",\"org\",\"workflow_engine\",\"core_hr\",\"leave\",\"calendar\",\"monitoring\",\"workforce\",\"verification\",\"exceptions\",\"analytics\",\"work_management\",\"chat\",\"chat_ai\",\"integrations\"]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert pillar fixes
            UpdateModulePillar(migrationBuilder, "chat_ai", "ai");
            UpdateModulePillar(migrationBuilder, "core_hr", "core_hr");
            UpdateModulePillar(migrationBuilder, "work_management", "work_management");

            // Remove all inserted rows
            var inserted = new[]
            {
                "auth", "configuration", "roles", "notifications", "org", "workflow_engine",
                "leave", "calendar",
                "monitoring", "workforce", "verification", "exceptions", "analytics",
                "chat", "integrations",
                "payroll", "performance", "skills", "learning", "recruitment",
                "hr_docs", "grievance", "expense", "documents", "reports"
            };

            foreach (var key in inserted)
            {
                migrationBuilder.DeleteData(table: "module_catalog", keyColumn: "module_key", keyColumnType: ModuleKeyType, keyValue: key);
            }

            // Revert subscription plan
            UpdatePlanModules(
                migrationBuilder,
                "[\"auth\",\"configuration\",\"roles\",\"notifications\",\"org\",\"core_hr\",\"leave\",\"calendar\",\"monitoring\",\"workforce\",\"verification\",\"exceptions\",\"analytics\",\"work_management\",\"chat\",\"chat_ai\",\"integrations\"]");
        }

        private static void UpdateModulePillar(MigrationBuilder migrationBuilder, string moduleKey, string pillar)
        {
            migrationBuilder.UpdateData(
                table: "module_catalog",
                keyColumns: new[] { "module_key" },
                keyColumnTypes: new[] { ModuleKeyType },
                keyValues: new object[] { moduleKey },
                columns: new[] { "pillar" },
                columnTypes: new[] { PillarType },
                values: new object[] { pillar });
        }

        private static void UpdatePlanModules(MigrationBuilder migrationBuilder, string includedModulesJson)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "uuid" },
                keyValues: new object[] { new Guid("a1b2c3d4-0001-0001-0001-000000000001") },
                columns: new[] { "included_modules_json" },
                columnTypes: new[] { "jsonb" },
                values: new object[] { includedModulesJson });
        }
    }
}
