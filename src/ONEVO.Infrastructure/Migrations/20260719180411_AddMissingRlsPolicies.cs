using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingRlsPolicies : Migration
    {
        // tenant_storage_stats and mfa_challenges were flagged as a known gap
        // in FILE_STORAGE_FOUNDATION_STEP_REPORT.md (never retrofitted into
        // AddRlsPolicies' table list). positions, user_integration_connections,
        // and tenant_integration_credentials were found to have the same gap
        // during the tenant isolation hardening audit. All five are
        // tenant-owned (implement ITenantOwnedEntity) with no RLS policy
        // before this migration. Uses the same admin-bypass tenant_isolation
        // policy pattern as UpdateRlsTenantContextMode, since these are core
        // tables read by both tenant-scoped and platform/admin code paths.
        private static readonly string[] TenantTables =
        [
            "tenant_storage_stats", "mfa_challenges", "positions",
            "user_integration_connections", "tenant_integration_credentials"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
        }
    }
}
