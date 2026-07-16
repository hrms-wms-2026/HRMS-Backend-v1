using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260520000000_UpdateRlsTenantContextMode")]
    public partial class UpdateRlsTenantContextMode : Migration
    {
        private static readonly string[] TenantTables =
        [
            "users", "roles", "role_permissions", "user_roles",
            "user_permission_overrides", "sessions", "refresh_tokens",
            "password_reset_tokens", "user_mfa", "audit_logs",
            "invitation_tokens", "tenant_auth_policies", "gdpr_consent_records",
            "user_external_identities", "feature_access_grants",
            "employees", "legal_entities", "tenant_subscriptions",
            "tenant_provisioning_states", "tenant_status_histories",
            "tenant_setup_selections", "tenant_one_time_charges"
        ];

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING      (tenant_id::text = current_setting('app.current_tenant_id', true))
                        WITH CHECK (tenant_id::text = current_setting('app.current_tenant_id', true));
                ");
            }
        }
    }
}
