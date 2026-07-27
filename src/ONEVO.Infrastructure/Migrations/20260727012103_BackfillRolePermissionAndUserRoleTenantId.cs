using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Data-correction migration, not a schema change. Six application code paths
    /// (DefaultRoleSeeder, TenantOwnerInvitationService, AdminCreateTenantRoleCommandHandler,
    /// AdminAssignTenantRolePermissionsCommandHandler, ApplyRoleTemplateCommandHandler,
    /// CreateRoleCommandHandler, AssignRolePermissionsCommandHandler) constructed RolePermission/
    /// UserRole rows without ever setting TenantId, so every such row written before the
    /// corresponding code fix has TenantId = '00000000-0000-0000-0000-000000000000' instead of
    /// its owning tenant's real id.
    ///
    /// This was invisible in practice before TenantDatabaseTicketStore.RetrieveAsync started
    /// correctly switching tenant context: ApplicationDbContext's ambient tenant/soft-delete query
    /// filter was inactive for that read path, so permission resolution silently ignored TenantId
    /// entirely and matched rows purely by RoleId (globally unique), producing the right answer by
    /// accident. Now that tenant-context switching is correct, the filter is active, and any
    /// pre-existing row with TenantId = Guid.Empty is (correctly) excluded - which means every
    /// tenant/role/user created before this migration would otherwise see empty role-derived
    /// permissions (e.g. "roles:read" required" 403s) the moment this deploys, until backfilled.
    ///
    /// Fix: for every RolePermission/UserRole row still at the empty-guid sentinel, copy the
    /// correct TenantId across from its own Role row (Role.TenantId has always been set correctly
    /// by every Role-creation code path). Idempotent - only rows still at the sentinel are touched,
    /// so re-running this migration (or applying it after further such rows leaked in) is safe.
    /// Down() is deliberately a no-op: there is no correct value to revert TenantId back to, and
    /// reintroducing Guid.Empty would only resurrect the bug this migration exists to fix.
    ///
    /// SET LOCAL app.tenant_context_mode = 'admin' is required around the UPDATEs. Migrations run
    /// as onevo_migrator, which is NOSUPERUSER NOBYPASSRLS (ops/postgres/local-bootstrap-roles.sql),
    /// and role_permissions/user_roles have FORCE ROW LEVEL SECURITY set
    /// (20260515022320_AddRlsPolicies.cs), so FORCE applies RLS even to onevo_migrator as the
    /// tables' owner. The current tenant_isolation policy
    /// (20260520000000_UpdateRlsTenantContextMode.cs) is
    /// `current_setting('app.tenant_context_mode', true) = 'admin' OR (... 'tenant' AND tenant_id
    /// matches)` - with neither GUC set (the normal state during migration execution, since nothing
    /// sets them outside a running request), a plain UPDATE's USING clause matches zero rows and the
    /// backfill silently no-ops. This was verified empirically against a throwaway Postgres
    /// container replicating onevo_migrator's exact privilege profile (NOBYPASSRLS, FORCE RLS,
    /// table owner): a naive UPDATE with no GUC set updated 0 rows; the same UPDATE wrapped in `SET
    /// LOCAL app.tenant_context_mode = 'admin'` updated the row and produced the correct TenantId.
    /// `SET LOCAL` (not the session-wide `SET`) means it only applies within this migration's own
    /// transaction and cannot leak onto a pooled connection reused afterward. This does not disable
    /// RLS, grant BYPASSRLS, or introduce any new bypass mechanism - `app.tenant_context_mode =
    /// 'admin'` is the same, already-existing admin-mode bypass the RLS policy has always granted to
    /// `IWritableTenantContext.SetAdminMode()` callers elsewhere in the application; this migration
    /// only invokes it via the GUC directly, since migrations don't go through that C# API.
    /// </summary>
    public partial class BackfillRolePermissionAndUserRoleTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE role_permissions rp
                SET tenant_id = r.tenant_id
                FROM roles r
                WHERE rp.role_id = r.id
                  AND rp.tenant_id = '00000000-0000-0000-0000-000000000000';

                UPDATE user_roles ur
                SET tenant_id = r.tenant_id
                FROM roles r
                WHERE ur.role_id = r.id
                  AND ur.tenant_id = '00000000-0000-0000-0000-000000000000';
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op: there is no correct prior value to restore, and reverting
            // TenantId back to Guid.Empty would only reintroduce the bug this migration fixes.
        }
    }
}
