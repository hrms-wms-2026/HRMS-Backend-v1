using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireSettingsNotificationsPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // role_permissions and user_permission_overrides both carry
            // ON DELETE CASCADE on their permission_id FK (see
            // PermissionConfiguration/UserPermissionOverrideConfiguration), so
            // deleting the permissions row below cascades those automatically.
            // module_permission_ownership has no FK to permissions, so it is
            // cleaned up explicitly first to avoid an orphaned ownership row.
            migrationBuilder.Sql(
                """
                DELETE FROM module_permission_ownership
                WHERE permission_code = 'settings:notifications';

                UPDATE role_templates
                SET permission_codes_json = permission_codes_json - 'settings:notifications'
                WHERE permission_codes_json ? 'settings:notifications';

                DELETE FROM permissions
                WHERE code = 'settings:notifications';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    RAISE EXCEPTION 'RetireSettingsNotificationsPermission is one-way because deleted tenant role grants and user overrides cannot be reconstructed safely.';
                END
                $migration$;
                """);
        }
    }
}
