using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Moves every persisted legacy module key to its canonical key (DEC-040, DEC-041, DEC-042,
    /// DEC-048): module JSON arrays (plans, subscriptions, role and configuration templates),
    /// permissions.module, module_permission_ownership, module_features and module_integration_links.
    /// Legacy module_catalog rows are deactivated, never deleted. feature_access_grants and
    /// module_catalog_price_history keep their historical values.
    ///
    /// JSON arrays are rewritten only when they are string arrays containing a legacy key; other
    /// values (corrupt or non-array JSON) are left for the entitlement service to deny. The data
    /// change is forward-only; Down restores only the starter plan's HasData value. Tenant tables
    /// are under RLS for every login, so the script runs in the 'admin' tenant context mode.
    /// </summary>
    public partial class ReconcileModuleIdentities : Migration
    {
        private static readonly (string Legacy, string Canonical)[] Aliases =
        [
            ("org_structure", "org"),
            ("leave", "time_off"),
            ("activity_monitoring", "monitoring"),
            ("desktop_agent_gateway", "monitoring"),
            ("discrepancy_engine", "monitoring"),
            ("exception_engine", "monitoring"),
            ("identity_verification", "verification"),
            ("productivity_analytics", "analytics"),
            ("worksync_foundation", "work_management"),
            ("projects", "work_management"),
            ("objectives_milestones", "work_management"),
            ("tasks", "work_management"),
            ("boards", "work_management"),
            ("planning_sprints", "work_management"),
            ("attendance", "time_attendance"),
            ("employees", "core_hr"),
        ];

        /// <summary>The reconciliation script. Idempotent; exposed for the SQL Server migration tests.</summary>
        public static readonly string UpSql = BuildUpSql();

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"org\",\"core_hr\",\"time_off\",\"calendar\",\"time_attendance\",\"monitoring\",\"verification\",\"analytics\",\"work_management\"]");

            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"org_structure\",\"core_hr\",\"leave\",\"calendar\",\"time_attendance\",\"monitoring\",\"discrepancy_engine\",\"identity_verification\",\"exception_engine\",\"productivity_analytics\",\"desktop_agent_gateway\",\"worksync_foundation\",\"projects\",\"objectives_milestones\",\"tasks\",\"boards\",\"planning_sprints\"]");
        }

        private static string BuildUpSql()
        {
            var aliasRows = string.Join(",\n    ", Aliases.Select(a => $"(N'{a.Legacy}', N'{a.Canonical}')"));
            return $"""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                DECLARE @alias TABLE (legacy nvarchar(100) NOT NULL PRIMARY KEY, canonical nvarchar(100) NOT NULL);
                INSERT INTO @alias (legacy, canonical) VALUES
                    {aliasRows};

                {RewriteJsonArray("subscription_plans", "included_modules_json")}
                {RewriteJsonArray("tenant_subscriptions", "selected_modules")}
                {RewriteJsonArray("role_templates", "module_keys_json")}
                {RewriteJsonArray("configuration_templates", "module_keys_json")}

                UPDATE p SET module = a.canonical
                FROM dbo.permissions p JOIN @alias a ON a.legacy = p.module;
                UPDATE dbo.permissions SET module = N'time_attendance'
                WHERE module = N'core_hr' AND code IN (N'attendance:read', N'attendance:write', N'attendance:approve');

                UPDATE o SET module_key = a.canonical
                FROM dbo.module_permission_ownership o JOIN @alias a ON a.legacy = o.module_key;

                UPDATE f SET module_key = a.canonical
                FROM dbo.module_features f JOIN @alias a ON a.legacy = f.module_key;

                WITH mapped AS (
                    SELECT l.module_key, l.integration_key,
                           ROW_NUMBER() OVER (
                               PARTITION BY COALESCE(a.canonical, l.module_key), l.integration_key
                               ORDER BY CASE WHEN a.legacy IS NULL THEN 0 ELSE 1 END, l.module_key) AS rn
                    FROM dbo.module_integration_links l
                    LEFT JOIN @alias a ON a.legacy = l.module_key)
                DELETE l
                FROM dbo.module_integration_links l
                JOIN mapped m ON m.module_key = l.module_key AND m.integration_key = l.integration_key
                WHERE m.rn > 1;
                UPDATE l SET module_key = a.canonical
                FROM dbo.module_integration_links l JOIN @alias a ON a.legacy = l.module_key;

                UPDATE c SET is_active = 0
                FROM dbo.module_catalog c JOIN @alias a ON a.legacy = c.module_key
                WHERE c.is_active = 1;

                EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
                """;
        }

        /// <summary>
        /// Rewrites a JSON array of module keys to canonical, trimmed, de-duplicated keys in first-seen
        /// order. Only string arrays that contain at least one legacy key are touched. OPENJSON always
        /// reads a guarded value, because SQL Server may evaluate it before the WHERE filter.
        /// </summary>
        private static string RewriteJsonArray(string table, string column)
        {
            var safe = $"CASE WHEN ISJSON(t.{column}) = 1 AND LEFT(LTRIM(t.{column}), 1) = N'[' THEN t.{column} ELSE N'[]' END";
            return $"""
                UPDATE t SET {column} = r.new_json
                FROM dbo.{table} t
                CROSS APPLY (
                    SELECT N'[' + STRING_AGG(CAST(N'"' + STRING_ESCAPE(k.module_key, 'json') + N'"' AS nvarchar(max)), N',')
                               WITHIN GROUP (ORDER BY k.first_ordinal) + N']' AS new_json
                    FROM (
                        SELECT COALESCE(a.canonical, LTRIM(RTRIM(j.value))) AS module_key,
                               MIN(CAST(j.[key] AS int)) AS first_ordinal
                        FROM OPENJSON({safe}) j
                        LEFT JOIN @alias a ON a.legacy = LTRIM(RTRIM(j.value))
                        WHERE LTRIM(RTRIM(j.value)) <> N''
                        GROUP BY COALESCE(a.canonical, LTRIM(RTRIM(j.value)))) k) r
                WHERE ISJSON(t.{column}) = 1
                  AND LEFT(LTRIM(t.{column}), 1) = N'['
                  AND NOT EXISTS (SELECT 1 FROM OPENJSON({safe}) j WHERE j.type <> 1)
                  AND EXISTS (SELECT 1 FROM OPENJSON({safe}) j JOIN @alias a ON a.legacy = LTRIM(RTRIM(j.value)));
                """;
        }
    }
}
