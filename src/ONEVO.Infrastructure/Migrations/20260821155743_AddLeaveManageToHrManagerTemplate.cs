using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveManageToHrManagerTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE role_templates
                SET permission_codes_json = permission_codes_json::jsonb || '["leave:manage"]'::jsonb
                WHERE name = 'HR Manager'
                  AND NOT (permission_codes_json::jsonb ? 'leave:manage');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE role_templates
                SET permission_codes_json = permission_codes_json::jsonb - 'leave:manage'
                WHERE name = 'HR Manager';
                """);
        }
    }
}
