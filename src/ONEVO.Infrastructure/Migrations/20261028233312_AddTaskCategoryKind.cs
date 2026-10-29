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

            // Existing categories get the kind their name suggests (TaskCategoryKinds.Infer); anything else stays "task".
            // task_categories has FORCE ROW LEVEL SECURITY, so without admin mode the tenant_isolation policy matches no rows
            // and the UPDATE silently changes nothing (as in 20260823175314_AddWorkTaskCategoryId). SET LOCAL scopes it to
            // this migration's transaction.
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                UPDATE task_categories SET kind = CASE
                    WHEN system_key = 'meeting' THEN 'meeting'
                    WHEN lower(btrim(name)) IN ('bug', 'bugs', 'defect', 'defects') THEN 'bug'
                    WHEN lower(btrim(name)) IN ('story', 'stories', 'user story') THEN 'story'
                    WHEN lower(btrim(name)) IN ('feature', 'features', 'enhancement') THEN 'feature'
                    WHEN lower(btrim(name)) IN ('test', 'tests', 'testing', 'qa') THEN 'testing'
                    WHEN lower(btrim(name)) IN ('onboarding', 'offboarding') THEN 'checklist'
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
