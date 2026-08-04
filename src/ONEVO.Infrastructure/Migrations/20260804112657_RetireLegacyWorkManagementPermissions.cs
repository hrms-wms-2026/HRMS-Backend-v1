using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireLegacyWorkManagementPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reassign every role_permissions row referencing a retired code to projects:access
            // instead, deduplicating if the role already has both (ON CONFLICT DO NOTHING avoids a
            // unique-constraint violation on (role_id, permission_id) when a role already holds
            // projects:access as well as one of the retired codes).
            //
            // DEVIATION FROM BRIEF: the brief's INSERT targeted columns (id, tenant_id, role_id,
            // permission_id, created_at), but role_permissions has no id or created_at column
            // (verified via `\d role_permissions` against the live local DB — the table's only
            // columns are role_id, permission_id, tenant_id, with PK (role_id, permission_id)).
            // The brief's SQL as literally written would fail with
            // "column \"id\" of relation \"role_permissions\" does not exist". Column list
            // corrected to match the actual schema; join/WHERE/ON CONFLICT logic is unchanged.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (tenant_id, role_id, permission_id)
                SELECT rp.tenant_id, rp.role_id, p_new.id
                FROM role_permissions rp
                JOIN permissions p_old ON p_old.id = rp.permission_id
                JOIN permissions p_new ON p_new.code = 'projects:access'
                WHERE p_old.code IN ('projects:write', 'projects:create',
                                      'members:read', 'members:manage',
                                      'invitations:manage', 'invitations:respond',
                                      'versions:write', 'labels:manage')
                ON CONFLICT (role_id, permission_id) DO NOTHING;

                DELETE FROM role_permissions
                WHERE permission_id IN (
                    SELECT id FROM permissions WHERE code IN (
                        'projects:write', 'projects:create',
                        'members:read', 'members:manage',
                        'invitations:manage', 'invitations:respond',
                        'versions:write', 'labels:manage')
                );

                DELETE FROM permissions
                WHERE code IN ('projects:write', 'projects:create',
                                'members:read', 'members:manage',
                                'invitations:manage', 'invitations:respond',
                                'versions:write', 'labels:manage');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no-op: re-inserting the retired permission rows on rollback would not
            // restore the original role_permissions grants that were merged into projects:access
            // (that mapping is lossy by design - a role could have held projects:write without
            // projects:create, and Up's dedup INSERT does not distinguish that on the way back).
            // A rollback of this migration must be a manual, reviewed DBA operation, not automatic.
        }
    }
}
