using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Tenant Google login by Google subject (DEC-062):
    /// - mfa_challenges.pending_external_identity_json carries a pending Google link through MFA;
    /// - auth_internal.auth_lookup_google_login_candidates is the sole pre-tenant Google lookup;
    /// - user_external_identities and tenant_auth_policies move to the Users predicate, so only the
    ///   lookup user can read them before a tenant is chosen (writes stay tenant-only).
    /// Grants for the procedure and the lookup user's column access are in
    /// ops/sqlserver/post-migration-grants.sql, because crownus_migrator cannot grant them.
    /// </summary>
    public partial class AddGoogleLoginSubjectLookup : Migration
    {
        private static readonly string[] LookupTables = ["user_external_identities", "tenant_auth_policies"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_external_identity_json",
                table: "mfa_challenges",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            foreach (var table in LookupTables)
            {
                migrationBuilder.Sql(RlsSql.DropPolicy(table));
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, RlsPredicate.Users));
            }

            // Eligible users linked to the Google subject. Only when there are none: eligible users with
            // the verified email who have no Google link at all (a user linked to a different subject is
            // never offered by email). Eligible = active user, Active/Trial tenant, and a policy that does
            // not disable Google. The policy filter is in SQL so TOP (9) counts eligible rows; the domain
            // list is returned for the C# check. Runs as a user with no login, which may read only the
            // granted columns. No dynamic SQL.
            migrationBuilder.Sql("""
                CREATE PROCEDURE auth_internal.auth_lookup_google_login_candidates
                    @provider_subject nvarchar(255),
                    @normalized_email nvarchar(255)
                WITH EXECUTE AS N'crownus_auth_base_login_fn_owner'
                AS
                BEGIN
                    SET NOCOUNT ON;
                    WITH eligible AS (
                    SELECT
                        c.tenant_id,
                        c.user_id,
                        t.slug,
                        t.name AS display_name,
                        c.matched_by_subject,
                        p.allowed_login_domains_json
                    FROM (
                        SELECT x.tenant_id, x.user_id, CAST(1 AS bit) AS matched_by_subject
                        FROM dbo.user_external_identities x
                        WHERE x.provider = N'google'
                          AND x.provider_subject = @provider_subject
                        UNION ALL
                        SELECT u.tenant_id, u.id AS user_id, CAST(0 AS bit) AS matched_by_subject
                        FROM dbo.users u
                        WHERE u.normalized_email = @normalized_email
                          AND NOT EXISTS (
                              SELECT 1 FROM dbo.user_external_identities x
                              WHERE x.tenant_id = u.tenant_id
                                AND x.user_id = u.id
                                AND x.provider = N'google')
                    ) c
                    JOIN dbo.users u ON u.id = c.user_id AND u.tenant_id = c.tenant_id
                    JOIN dbo.tenants t ON t.id = c.tenant_id
                    LEFT JOIN dbo.tenant_auth_policies p ON p.tenant_id = c.tenant_id
                    WHERE u.is_active = 1
                      AND u.is_deleted = 0
                      AND t.status IN (N'Active', N'Trial')
                      AND (p.google_completion_allowed IS NULL OR p.google_completion_allowed = 1)
                    )
                    SELECT TOP (9)
                        e.tenant_id,
                        e.user_id,
                        e.slug,
                        e.display_name,
                        e.matched_by_subject,
                        e.allowed_login_domains_json
                    FROM eligible e
                    WHERE e.matched_by_subject = 1
                       OR NOT EXISTS (SELECT 1 FROM eligible s WHERE s.matched_by_subject = 1)
                    ORDER BY e.tenant_id, e.user_id;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE auth_internal.auth_lookup_google_login_candidates;");

            foreach (var table in LookupTables)
            {
                migrationBuilder.Sql(RlsSql.DropPolicy(table));
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, RlsPredicate.Tenant));
            }

            migrationBuilder.DropColumn(
                name: "pending_external_identity_json",
                table: "mfa_challenges");
        }
    }
}
