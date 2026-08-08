using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowSystemModeOnTrayActivationRls : Migration
    {
        // Device-code enrollment is anonymous by design (the tenant is unknown
        // until the activation-code hash resolves it — see
        // ExchangeActivationCodeCommandHandler) and the device-JWT-authenticated
        // revoke endpoint is likewise called at the root domain with no tenant
        // subdomain, so both land in TenantContextMode.System. The original
        // tenant_isolation policy on these three tables only allowed 'admin' or
        // a matching 'tenant' context through, which silently blocked every row
        // for 'system' mode — exchange/refresh/revoke all failed against a real
        // RLS-enforced database despite valid data existing. This adds a
        // 'system' bypass scoped to only these three tables; every other
        // tenant-owned table keeps the original admin-or-matching-tenant policy.
        private static readonly string[] TrayActivationTables =
        [
            "tray_activation_codes",
            "tray_device_registrations",
            "tray_device_refresh_tokens"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TrayActivationTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR current_setting('app.tenant_context_mode', true) = 'system'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR current_setting('app.tenant_context_mode', true) = 'system'
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
            foreach (var table in TrayActivationTables)
            {
                migrationBuilder.Sql($@"
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
    }
}
