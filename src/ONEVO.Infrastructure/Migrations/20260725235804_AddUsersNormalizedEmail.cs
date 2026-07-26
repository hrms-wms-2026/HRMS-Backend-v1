using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersNormalizedEmail : Migration
    {
        private const string FunctionOwnerRole = "onevo_auth_base_login_fn_owner";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Duplicate precheck: fail loudly, before the unique index is ever created, if any
            // tenant already has two users whose case/whitespace-insensitive email collides. Never
            // silently deletes/merges/modifies users - this is a hard stop requiring manual cleanup.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    v_duplicate_count integer;
                BEGIN
                    SELECT count(*) INTO v_duplicate_count
                    FROM (
                        SELECT tenant_id, lower(trim(email)) AS normalized
                        FROM public.users
                        GROUP BY tenant_id, lower(trim(email))
                        HAVING count(*) > 1
                    ) AS duplicates;

                    IF v_duplicate_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot add unique index on users(tenant_id, normalized_email): % tenant(s) have two or more users whose lower(trim(email)) values collide. Resolve duplicate emails before running this migration.',
                            v_duplicate_count;
                    END IF;
                END;
                $$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "normalized_email",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                computedColumnSql: "lower(trim(email))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email_active_lookup",
                table: "users",
                columns: new[] { "normalized_email", "tenant_id", "id" },
                filter: "is_active = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_normalized_email",
                table: "users",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true);

            // Base-domain lookup now compares the DB-generated normalized_email column instead of
            // email, so casing/whitespace is enforced by the database rather than trusted app input.
            migrationBuilder.Sql(
                """
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
                    WHERE u.normalized_email = p_normalized_email
                      AND u.is_active = true
                      AND u.is_deleted = false
                      AND t.status IN ('Active', 'Trial')
                    ORDER BY u.tenant_id, u.id
                    LIMIT 9;
                $$;
                """);

            // Column-level grants only: normalized_email replaces email in the least-privilege
            // column list, since the function no longer references email at all.
            migrationBuilder.Sql($"REVOKE SELECT ON public.users FROM {FunctionOwnerRole};");
            migrationBuilder.Sql(
                $"GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash) ON public.users TO {FunctionOwnerRole};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert the function/grant to the email-based definition before dropping
            // normalized_email, so the function never references a column that no longer exists.
            migrationBuilder.Sql(
                """
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
            migrationBuilder.Sql($"REVOKE SELECT ON public.users FROM {FunctionOwnerRole};");
            migrationBuilder.Sql(
                $"GRANT SELECT (tenant_id, id, email, is_active, is_deleted, password_hash) ON public.users TO {FunctionOwnerRole};");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email_active_lookup",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_tenant_id_normalized_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_email",
                table: "users");
        }
    }
}
