using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// The backfill statements below run as onevo_migrator, which is NOSUPERUSER NOBYPASSRLS
    /// (ops/postgres/local-bootstrap-roles.sql), and tasks/task_categories both have FORCE ROW LEVEL
    /// SECURITY, so RLS applies even to onevo_migrator as the tables' owner. Per the same precedent as
    /// 20260727012103_BackfillRolePermissionAndUserRoleTenantId.cs, without an admin-mode GUC set the
    /// tenant_isolation policy's USING clause matches zero rows and the backfill would silently no-op
    /// (verified empirically here: the first run without SET LOCAL left category_id NULL on every
    /// synthetic row and the later AlterColumn NOT NULL step failed with 23502). SET LOCAL scopes the
    /// override to this migration's own transaction only.
    ///
    /// Before the task_type-to-category UPDATE, this migration also self-heals any (tenant_id,
    /// project_id) pair that has tasks but zero task_categories rows for that project - e.g. a project
    /// created by a code path that seeds TaskStatus rows directly without going through
    /// CreateProjectCommandHandler's default-TaskCategory-template seeding (this gap is real: it was
    /// found against this repo's own local dev DB, where a demo-data seeding pass populated 135 tasks
    /// across 11 projects with TaskStatus rows but no TaskCategory rows at all, which would otherwise
    /// leave every one of those tasks' category_id permanently NULL and fail the AlterColumn NOT NULL
    /// step below). The self-heal inserts the same 4 default categories
    /// (DefaultTaskCategoryTemplate.BuildRows's exact shape: name/display_order = task/0, bug/1,
    /// story/2, feature/3) for any project missing them, guarded by NOT EXISTS per (tenant_id,
    /// project_id, name) so it never collides with ix_task_categories_one_name_per_project and never
    /// duplicates categories a project already has (partial or full). This makes the migration safe and
    /// idempotent against any database it is ever applied to, not just this local dev DB's current
    /// state.
    /// </summary>
    public partial class AddWorkTaskCategoryId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                SET LOCAL app.tenant_context_mode = 'admin';

                INSERT INTO task_categories (id, tenant_id, project_id, name, display_order, created_at, created_by_id, is_deleted)
                SELECT gen_random_uuid(), proj.tenant_id, proj.project_id, cat.name, cat.display_order, now(), proj.created_by_id, false
                FROM (
                    SELECT DISTINCT ON (tenant_id, project_id) tenant_id, project_id, created_by_id
                    FROM tasks
                    ORDER BY tenant_id, project_id, created_at
                ) proj
                CROSS JOIN (VALUES ('task', 0), ('bug', 1), ('story', 2), ('feature', 3)) AS cat(name, display_order)
                WHERE NOT EXISTS (
                    SELECT 1 FROM task_categories c
                    WHERE c.tenant_id = proj.tenant_id AND c.project_id = proj.project_id AND c.name = cat.name
                );

                UPDATE tasks t
                SET category_id = c.id
                FROM task_categories c
                WHERE t.project_id = c.project_id
                  AND c.name = t.task_type;
            ");

            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                table: "tasks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_category_id",
                table: "tasks",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_id_project_id_category_id",
                table: "tasks",
                columns: new[] { "tenant_id", "project_id", "category_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_task_categories_category_id",
                table: "tasks",
                column: "category_id",
                principalTable: "task_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_task_categories_category_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_category_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_id_project_id_category_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "tasks");
        }
    }
}
