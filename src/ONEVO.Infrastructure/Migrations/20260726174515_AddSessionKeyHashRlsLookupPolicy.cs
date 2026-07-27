using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Adds a second, narrowly-scoped permissive RLS policy on "sessions" for SELECT only, matching
    /// on the session's own key_hash rather than tenant_id. This coexists with (does not replace)
    /// tenant_isolation - Postgres OR-combines multiple permissive policies for the same command, so
    /// either one being satisfied grants row visibility; tenant_isolation alone still governs
    /// INSERT/UPDATE/DELETE.
    ///
    /// This exists because TenantDatabaseTicketStore.RetrieveAsync/RemoveAsync only ever have a
    /// session key (from the auth cookie) when they run - never a tenant id - so they cannot set
    /// app.current_tenant_id before the very first lookup that would tell them which tenant the
    /// session belongs to. key_hash is a SHA-256 hash of a 32-byte cryptographically random value
    /// that only ever leaves the server inside an HttpOnly cookie; presenting it back is itself the
    /// proof of authorization, independent of tenant_id. EfAuthRepository is the only caller that ever
    /// sets app.session_lookup_key_hash (via GetByKeyHashForTenantResolutionAsync, is_local so it
    /// reverts automatically at transaction end - never left set on a pooled connection), and it does
    /// so exactly to satisfy this policy for that one bootstrap lookup; TenantDatabaseTicketStore then
    /// switches to the resolved tenant context before doing anything else.
    /// </summary>
    public partial class AddSessionKeyHashRlsLookupPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS session_key_lookup ON sessions;
                CREATE POLICY session_key_lookup ON sessions
                    FOR SELECT
                    USING (key_hash = current_setting('app.session_lookup_key_hash', true));
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS session_key_lookup ON sessions;
            """);
        }
    }
}
