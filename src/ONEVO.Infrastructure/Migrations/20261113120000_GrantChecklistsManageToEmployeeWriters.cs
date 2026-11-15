using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Creating, editing and archiving checklist templates moved from employees:write to its own
    /// checklists:manage code. So nobody loses access on rollout, every role that holds
    /// employees:write today also gets checklists:manage; admins can take it away per role after.
    ///
    /// The permission row is created here when missing (PermissionSeeder only runs at startup,
    /// after migrations). role_permissions is under tenant RLS, which applies to every login on
    /// SQL Server, so the copy runs in the 'admin' tenant context mode - without it the INSERT
    /// would see no rows and silently grant nothing.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261113120000_GrantChecklistsManageToEmployeeWriters")]
    public class GrantChecklistsManageToEmployeeWriters : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                DECLARE @manage uniqueidentifier = (SELECT id FROM dbo.permissions WHERE code = N'checklists:manage');
                IF @manage IS NULL
                BEGIN
                    SET @manage = NEWID();
                    INSERT INTO dbo.permissions (id, code, description, module)
                    VALUES (@manage, N'checklists:manage',
                            N'Create, edit, and archive onboarding and offboarding checklist templates.', N'core_hr');
                END;

                INSERT INTO dbo.role_permissions (role_id, permission_id, tenant_id)
                SELECT rp.role_id, @manage, rp.tenant_id
                FROM dbo.role_permissions rp
                INNER JOIN dbo.permissions p ON p.id = rp.permission_id
                WHERE p.code = N'employees:write'
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.role_permissions existing
                      WHERE existing.role_id = rp.role_id AND existing.permission_id = @manage);

                EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
                """);
        }

        /// <summary>Not reversed: after rollout, admins may grant or remove checklists:manage per role,
        /// and a down migration cannot tell those choices apart from the copied grants.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
