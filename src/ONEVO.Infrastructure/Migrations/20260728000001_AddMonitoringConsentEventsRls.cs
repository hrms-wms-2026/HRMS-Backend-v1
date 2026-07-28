using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringConsentEventsRls : Migration
    {
        private const string Table = "monitoring_consent_events";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                ALTER TABLE {Table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {Table} FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON {Table};
                CREATE POLICY tenant_isolation ON {Table}
                    USING (
                        current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    )
                    WITH CHECK (
                        current_setting('app.tenant_context_mode', true) IN ('admin', 'system')
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DROP POLICY IF EXISTS tenant_isolation ON {Table};
                ALTER TABLE {Table} DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
