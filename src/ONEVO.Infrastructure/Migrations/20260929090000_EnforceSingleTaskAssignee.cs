using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929090000_EnforceSingleTaskAssignee")]
public partial class EnforceSingleTaskAssignee : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM task_assignments
            WHERE id IN (
                SELECT id
                FROM (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY task_id
                               ORDER BY assigned_at DESC, id DESC
                           ) AS row_number
                    FROM task_assignments
                ) ranked
                WHERE ranked.row_number > 1
            );
            """);

        migrationBuilder.DropIndex(
            name: "ix_task_assignments_one_per_task_user",
            table: "task_assignments");

        migrationBuilder.CreateIndex(
            name: "ix_task_assignments_one_per_task",
            table: "task_assignments",
            column: "task_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_task_assignments_one_per_task",
            table: "task_assignments");

        migrationBuilder.CreateIndex(
            name: "ix_task_assignments_one_per_task_user",
            table: "task_assignments",
            columns: new[] { "task_id", "user_id" },
            unique: true);
    }
}
