using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SingleReactionPerCommentEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id_emoji",
                table: "task_comment_reactions");

            // Employees could previously stack several emoji on one comment; keep only their most
            // recent reaction so the new one-reaction-per-employee unique index can be created.
            migrationBuilder.Sql(@"
DELETE FROM task_comment_reactions r
USING (
    SELECT id, ROW_NUMBER() OVER (
        PARTITION BY comment_id, employee_id
        ORDER BY COALESCE(updated_at, created_at) DESC, id DESC) AS rn
    FROM task_comment_reactions
) ranked
WHERE r.id = ranked.id AND ranked.rn > 1;");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id",
                table: "task_comment_reactions",
                columns: new[] { "comment_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id",
                table: "task_comment_reactions");

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id_emoji",
                table: "task_comment_reactions",
                columns: new[] { "comment_id", "employee_id", "emoji" },
                unique: true);
        }
    }
}
