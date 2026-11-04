using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCategoryKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "task_categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "task");

            // Existing categories get the kind their name suggests; anything else stays "task". Checklist is a system kind,
            // so only the Office project's Onboarding/Offboarding get it: a user's "Onboarding" category elsewhere is a task.
            // task_categories has FORCE ROW LEVEL SECURITY, so without admin mode the tenant_isolation policy matches no rows
            // and the UPDATE silently changes nothing (as in 20260823175314_AddWorkTaskCategoryId). SET LOCAL scopes it to
            // this migration's transaction.
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE task_categories AS c SET kind = CASE
                    WHEN c.system_key = 'meeting' THEN 'meeting'
                    WHEN lower(btrim(c.name)) IN ('bug', 'bugs', 'defect', 'defects') THEN 'bug'
                    WHEN lower(btrim(c.name)) IN ('story', 'stories', 'user story') THEN 'story'
                    WHEN lower(btrim(c.name)) IN ('feature', 'features', 'enhancement') THEN 'feature'
                    WHEN lower(btrim(c.name)) IN ('test', 'tests', 'testing', 'qa') THEN 'testing'
                    WHEN lower(btrim(c.name)) IN ('onboarding', 'offboarding')
                        AND EXISTS (SELECT 1 FROM projects AS p WHERE p.id = c.project_id AND p.system_purpose = 'office')
                        THEN 'checklist'
                    ELSE 'task'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "task_categories");
        }
    }
}
