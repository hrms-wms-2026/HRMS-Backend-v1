using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    public partial class AddAuthLookupBaseLoginCandidatesFunction : Migration
    {
        private const string FunctionOwnerRole = "onevo_auth_base_login_fn_owner";
        private const string RuntimeAppRole = "onevo_app";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sole pre-tenant candidate lookup path. SECURITY DEFINER runs with the owning
            // role's privileges (BYPASSRLS, granted only via ops/postgres/local-bootstrap-roles.sql
            // and its production/staging equivalent), independent of the caller's RLS session
            // GUCs. search_path is pinned so no other schema can shadow "users"/"tenants". No
            // dynamic SQL. Eligibility is tenant status (Active/Trial) + active user only - no
            // tenant_auth_policies flag gates password/Google login availability (product rule).
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS auth_internal;
                REVOKE CREATE ON SCHEMA auth_internal FROM PUBLIC;
                REVOKE CREATE ON SCHEMA auth_internal FROM onevo_app;
                GRANT USAGE ON SCHEMA auth_internal TO onevo_app;

                CREATE OR REPLACE FUNCTION auth_internal.auth_lookup_base_login_candidates(p_normalized_email varchar(320))
                RETURNS TABLE (
                    tenant_id uuid,
                    user_id uuid,
                    slug varchar(100),
                    display_name varchar(200),
                    password_hash varchar(255)
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = pg_catalog
                AS $$
                    SELECT u.tenant_id, u.id AS user_id, t.slug, t.name AS display_name, u.password_hash
                    FROM public.users u
                    JOIN public.tenants t ON t.id = u.tenant_id
                    WHERE u.email = p_normalized_email
                      AND u.is_active = true
                      AND u.is_deleted = false
                      AND t.status IN ('Active', 'Trial')
                    ORDER BY u.tenant_id, u.id
                    LIMIT 9;
                $$;
                """);

            // Production-safety boundary: this migration assumes onevo_auth_base_login_fn_owner
            // and onevo_app already exist. It must NEVER create them itself - privileged role
            // provisioning is a separate, explicit deploy-time step
            // (ops/postgres/local-bootstrap-roles.sql locally; an equivalent DB/deployment-owned
            // bootstrap in staging/production), run by a human/pipeline with the authority to
            // create roles, before migrations run. onevo_migrator itself is NOCREATEROLE and
            // cannot create these roles even if it tried. If either role is missing, the ALTER
            // FUNCTION/GRANT statements below fail loudly with a clear Postgres
            // "role ... does not exist" error - not a silent self-provision. Required deploy order:
            // 1) run the privileged DB role bootstrap, 2) run EF migrations as onevo_migrator,
            // 3) run the API as onevo_app. Testcontainers-backed integration tests replicate step 1
            // via PrivilegedRoleTestBootstrap before calling Database.MigrateAsync().

            // Column-level grants only: this role must be able to read exactly the columns
            // auth_lookup_base_login_candidates references, not every column on users/tenants
            // (e.g. never mfa_secret or other users columns beyond what login eligibility needs).
            migrationBuilder.Sql($"REVOKE SELECT ON public.users, public.tenants FROM {FunctionOwnerRole};");
            migrationBuilder.Sql(
                $"GRANT SELECT (tenant_id, id, email, is_active, is_deleted, password_hash) ON public.users TO {FunctionOwnerRole};");
            migrationBuilder.Sql(
                $"GRANT SELECT (id, slug, name, status) ON public.tenants TO {FunctionOwnerRole};");
            migrationBuilder.Sql($"GRANT USAGE ON SCHEMA auth_internal TO {FunctionOwnerRole};");
            migrationBuilder.Sql($"ALTER FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar) OWNER TO {FunctionOwnerRole};");
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar) FROM PUBLIC;");
            migrationBuilder.Sql($"GRANT EXECUTE ON FUNCTION auth_internal.auth_lookup_base_login_candidates(varchar) TO {RuntimeAppRole};");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_internal.auth_lookup_base_login_candidates(varchar);");
        }
    }
}
