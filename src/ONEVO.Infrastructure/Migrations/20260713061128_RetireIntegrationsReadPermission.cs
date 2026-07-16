using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireIntegrationsReadPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM module_permission_ownership
                WHERE permission_code = 'integrations:read';

                UPDATE role_templates
                SET permission_codes_json = permission_codes_json - 'integrations:read'
                WHERE permission_codes_json ? 'integrations:read';

                DELETE FROM permissions
                WHERE code = 'integrations:read';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    RAISE EXCEPTION 'RetireIntegrationsReadPermission is one-way because deleted tenant role grants and user overrides cannot be reconstructed safely.';
                END
                $migration$;
                """);
        }
    }
}
