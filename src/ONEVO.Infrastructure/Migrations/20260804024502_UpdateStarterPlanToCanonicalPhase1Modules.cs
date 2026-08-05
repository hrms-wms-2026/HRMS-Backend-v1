using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStarterPlanToCanonicalPhase1Modules : Migration
    {
        // This migration has no .Designer.cs target-model diff for module_catalog (it is
        // seeded imperatively by ModuleCatalogSeeder / earlier data-only migrations, not via
        // HasData), so every module_catalog operation must carry explicit column types for
        // EF to generate SQL. Column set matches the CURRENT schema after
        // 20260709052426_AddModuleCatalogFoundation renamed price_brackets -> storage_reference
        // and dropped full_license_price/maintenance_rate in favor of pricing_reference/
        // storage_reference/ai_token_reference - NOT the older, now-stale column set used by
        // 20260522000001_FixAndSeedAllPhaseOneModuleCatalog (which ran before that rename).
        private static readonly string[] ModuleCatalogColumnTypes =
        [
            "character varying(100)",   // module_key
            "character varying(150)",   // name
            "character varying(50)",    // pillar
            "character varying(30)",    // phase
            "character varying(30)",    // pricing_unit
            "jsonb",                    // pricing_reference
            "jsonb",                    // storage_reference
            "jsonb",                    // ai_token_reference
            "boolean",                  // is_active
            "timestamp with time zone", // created_at
            "timestamp with time zone"  // updated_at
        ];

        private const string ModuleKeyType = "character varying(100)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"org_structure\",\"core_hr\",\"leave\",\"calendar\",\"time_attendance\",\"activity_monitoring\",\"discrepancy_engine\",\"identity_verification\",\"exception_engine\",\"productivity_analytics\",\"desktop_agent_gateway\",\"worksync_foundation\",\"projects\",\"objectives_milestones\",\"tasks\",\"boards\",\"planning_sprints\"]");

            // ── Insert the canonical Phase 1 product module catalog rows that did not
            //    previously exist under any name (org_structure/time_attendance for HR;
            //    the whole Workforce Intelligence and WorkSync product vocabularies).
            //    Pricing is intentionally left as an unset placeholder ("[]" pricing/storage/
            //    ai-token references) - this task's scope is module *membership*, not Phase 1
            //    commercial pricing, which was never provided. See
            //    PHASE1_SUBSCRIPTION_MODULE_SEED_RECONCILIATION_REPORT.md.
            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "name", "pillar", "phase", "pricing_unit", "pricing_reference", "storage_reference", "ai_token_reference", "is_active", "created_at", "updated_at" },
                columnTypes: ModuleCatalogColumnTypes,
                values: new object[,]
                {
                    { "org_structure",          "Org Structure",            "hr_management",          "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "time_attendance",        "Time & Attendance",        "hr_management",          "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "activity_monitoring",    "Activity Monitoring",      "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "discrepancy_engine",     "Discrepancy Engine",       "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "identity_verification",  "Identity Verification",    "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "exception_engine",       "Exception Engine",         "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "productivity_analytics", "Productivity Analytics",   "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "desktop_agent_gateway",  "Desktop Agent Gateway",    "workforce_intelligence", "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "worksync_foundation",    "WorkSync Foundation",      "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "projects",               "Projects",                 "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "objectives_milestones",  "Objectives & Milestones",  "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "tasks",                  "Tasks",                    "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "boards",                 "Boards",                   "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { "planning_sprints",       "Planning & Sprints",       "worksync",               "phase_1", "flat_rate", "[]", "[]", "[]", true, new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var inserted = new[]
            {
                "org_structure", "time_attendance", "activity_monitoring", "discrepancy_engine",
                "identity_verification", "exception_engine", "productivity_analytics",
                "desktop_agent_gateway", "worksync_foundation", "projects",
                "objectives_milestones", "tasks", "boards", "planning_sprints"
            };

            foreach (var key in inserted)
            {
                migrationBuilder.DeleteData(table: "module_catalog", keyColumn: "module_key", keyColumnType: ModuleKeyType, keyValue: key);
            }

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"auth\",\"configuration\",\"roles\",\"notifications\",\"org\",\"workflow_engine\",\"core_hr\",\"leave\",\"calendar\",\"monitoring\",\"workforce\",\"verification\",\"exceptions\",\"analytics\",\"work_management\",\"chat\",\"chat_ai\",\"integrations\"]");
        }
    }
}
