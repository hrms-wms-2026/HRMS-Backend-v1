using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationChangeRequestsRlsPolicyCoverage : Migration
    {
        // employee_work_locations and location_change_requests were created in
        // AddLocationChangeRequestsAndRemoteWorkLocation without RLS policies, missed at the time.
        // Adding them here via the standard TenantTables + foreach convention, matching how
        // AddObjectiveChangeRequestsRlsPolicyCoverage/AddConfigurationTemplateApplicationsRlsPolicy
        // fixed the same class of gap for other tables -
        // TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage parses
        // this exact `TenantTables = [...]` shape to know what's covered.
        private static readonly string[] TenantTables =
        [
            "employee_work_locations",
            "location_change_requests"
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
