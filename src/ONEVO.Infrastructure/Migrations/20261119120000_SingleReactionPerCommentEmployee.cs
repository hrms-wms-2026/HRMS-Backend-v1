using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// One reaction per employee per comment: reacting again replaces the emoji. SQL Server port of
    /// the PostgreSQL migration 20260924083825_SingleReactionPerCommentEmployee from main, which the
    /// SQL Server baseline (20261107081808_InitialSqlServer) predates.
    ///
    /// Employees could previously stack several emoji on one comment, so only their most recent
    /// reaction is kept before the new unique index is created. task_comment_reactions is under
    /// tenant RLS, which applies to every login on SQL Server, so the clean-up runs in the 'admin'
    /// tenant context mode; without it the DELETE would see no rows and the index creation would fail.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261119120000_SingleReactionPerCommentEmployee")]
    public class SingleReactionPerCommentEmployee : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id_emoji",
                table: "task_comment_reactions");

            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (
                        PARTITION BY comment_id, employee_id
                        ORDER BY COALESCE(updated_at, created_at) DESC, id DESC) AS rn
                    FROM dbo.task_comment_reactions
                )
                DELETE FROM ranked WHERE rn > 1;

                EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_task_comment_reactions_comment_id_employee_id",
                table: "task_comment_reactions",
                columns: new[] { "comment_id", "employee_id" },
                unique: true);
        }

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
