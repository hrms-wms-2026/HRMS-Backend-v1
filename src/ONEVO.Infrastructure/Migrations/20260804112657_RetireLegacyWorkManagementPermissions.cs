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
            // DEPLOY RUNBOOK WARNING (final-review finding #5 - documented here, not code-fixed):
            // already-issued JWTs/sessions carry the retired codes (e.g. 'projects:create') baked
            // into their permission claims. Those claims are only refreshed on next login/token
            // refresh, so users with a live session at deploy time will keep acting on stale
            // claims until they re-authenticate. This migration cannot invalidate live sessions;
            // the deployer must plan for a forced re-auth (or accept the staleness window) when
            // this ships. No session-invalidation code change is in scope here.

            // Seed projects:access before anything below references it, so this migration is
            // self-contained and does not depend on PermissionSeeder (an IHostedService that only
            // runs at application startup - i.e. strictly *after* migrations are applied) having
            // already created the row. Without this, on any database where this migration is the
            // first thing to run, the role_permissions reassignment JOIN below matches zero rows,
            // making the reassignment a no-op while the DELETEs still fire - silently stripping
            // every Work Management grant with nothing to replace it. permissions.code has a
            // unique index, so ON CONFLICT DO NOTHING keeps this idempotent regardless of whether
            // the seeder already beat us to it on this database. Description/module text mirrors
            // PermissionSeeder.GetAllPermissions()'s projects:access entry exactly, so re-running
            // the seeder afterward is a no-op update, not a second seed.
            //
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
                INSERT INTO permissions (id, code, description, module)
                VALUES (
                    gen_random_uuid(),
                    'projects:access',
                    'Work Management module access — create/edit/delete your own projects and milestones.',
                    'work_management'
                )
                ON CONFLICT (code) DO NOTHING;

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

                -- Per-user permission overrides (grants/revokes) on a retired code would otherwise
                -- be silently cascade-deleted by permissions' ON DELETE CASCADE to
                -- user_permission_overrides once the DELETE FROM permissions below runs, with no
                -- projects:access equivalent created. Reassign first, same dedup shape as the
                -- role_permissions reassignment above, adapted to this table's surrogate `id` PK
                -- and its actual unique constraint on (tenant_id, user_id, permission_id).
                INSERT INTO user_permission_overrides (id, tenant_id, user_id, permission_id, grant_type, reason, granted_by, created_at)
                SELECT gen_random_uuid(), o.tenant_id, o.user_id, p_new.id, o.grant_type, o.reason, o.granted_by, o.created_at
                FROM user_permission_overrides o
                JOIN permissions p_old ON p_old.id = o.permission_id
                JOIN permissions p_new ON p_new.code = 'projects:access'
                WHERE p_old.code IN ('projects:write', 'projects:create',
                                      'members:read', 'members:manage',
                                      'invitations:manage', 'invitations:respond',
                                      'versions:write', 'labels:manage')
                ON CONFLICT (tenant_id, user_id, permission_id) DO NOTHING;

                DELETE FROM user_permission_overrides
                WHERE permission_id IN (
                    SELECT id FROM permissions WHERE code IN (
                        'projects:write', 'projects:create',
                        'members:read', 'members:manage',
                        'invitations:manage', 'invitations:respond',
                        'versions:write', 'labels:manage')
                );

                -- role_templates.permission_codes_json is a jsonb array of permission code
                -- strings (see RoleTemplateConfiguration/RoleTemplate.PermissionCodesJson). Any
                -- template still storing one of the 8 retired codes becomes permanently
                -- un-appliable/un-editable after the DELETE FROM permissions below, because
                -- ApplyRoleTemplateCommandHandler / RoleTemplateValidation.ValidatePermissionCodeList
                -- hard-reject unknown codes. Rewrite every retired-code element to
                -- 'projects:access' and de-duplicate via jsonb_agg(DISTINCT ...) (a template that
                -- listed e.g. both 'projects:write' and 'projects:create' would otherwise end up
                -- with 'projects:access' twice). Element order is not semantically meaningful here
                -- (RoleTemplateValidation treats the list as an unordered set via .Distinct()), so
                -- DISTINCT's re-sort is safe. The WHERE EXISTS guard makes this idempotent - a
                -- second run finds no retired codes left and updates nothing.
                UPDATE role_templates rt
                SET permission_codes_json = (
                    SELECT COALESCE(jsonb_agg(DISTINCT mapped.code), '[]'::jsonb)
                    FROM (
                        SELECT CASE
                                   WHEN elem.value IN ('projects:write', 'projects:create',
                                                        'members:read', 'members:manage',
                                                        'invitations:manage', 'invitations:respond',
                                                        'versions:write', 'labels:manage')
                                   THEN 'projects:access'
                                   ELSE elem.value
                               END AS code
                        FROM jsonb_array_elements_text(rt.permission_codes_json) AS elem(value)
                    ) mapped
                )
                WHERE EXISTS (
                    SELECT 1
                    FROM jsonb_array_elements_text(rt.permission_codes_json) AS elem(value)
                    WHERE elem.value IN ('projects:write', 'projects:create',
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
