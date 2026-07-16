using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenIntegrationConnectionIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tenant_integration_credentials ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenant_integration_credentials FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON tenant_integration_credentials;
                CREATE POLICY tenant_isolation ON tenant_integration_credentials
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

                ALTER TABLE user_integration_connections ENABLE ROW LEVEL SECURITY;
                ALTER TABLE user_integration_connections FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON user_integration_connections;
                CREATE POLICY tenant_isolation ON user_integration_connections
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON user_integration_connections;
                ALTER TABLE user_integration_connections DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation ON tenant_integration_credentials;
                ALTER TABLE tenant_integration_credentials DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
