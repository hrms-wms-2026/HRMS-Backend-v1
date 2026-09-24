using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDatesAndHybridMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A module (objective) may now be a whole-member of many active events (spec §2, R1):
            // drop the unique composite index and recreate it non-unique.
            migrationBuilder.DropIndex(
                name: "ix_calendar_event_objectives_event_objective",
                table: "calendar_event_objectives");

            // Event duration (spec §4). Added nullable, back-filled from the linked objectives'
            // date span, then made NOT NULL - so existing dapi events stay valid under R2.
            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "calendar_events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "calendar_events",
                type: "date",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE calendar_events ce SET
                    start_date = COALESCE(sub.min_start, CURRENT_DATE),
                    end_date   = COALESCE(sub.max_end,   CURRENT_DATE)
                FROM (
                    SELECT ceo.calendar_event_id,
                           MIN(o.start_date) AS min_start,
                           MAX(o.end_date)   AS max_end
                    FROM calendar_event_objectives ceo
                    JOIN objectives o ON o.id = ceo.objective_id
                    GROUP BY ceo.calendar_event_id
                ) sub
                WHERE sub.calendar_event_id = ce.id;");
            migrationBuilder.Sql("UPDATE calendar_events SET start_date = CURRENT_DATE WHERE start_date IS NULL;");
            migrationBuilder.Sql("UPDATE calendar_events SET end_date = CURRENT_DATE WHERE end_date IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "calendar_events",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "end_date",
                table: "calendar_events",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "calendar_event_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_event_tasks_calendar_events_calendar_event_id",
                        column: x => x.calendar_event_id,
                        principalTable: "calendar_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_event_tasks_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_objectives_event_objective",
                table: "calendar_event_objectives",
                columns: new[] { "calendar_event_id", "objective_id" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_tasks_event_task",
                table: "calendar_event_tasks",
                columns: new[] { "calendar_event_id", "task_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_tasks_task_id",
                table: "calendar_event_tasks",
                column: "task_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_event_tasks");

            migrationBuilder.DropIndex(
                name: "ix_calendar_event_objectives_event_objective",
                table: "calendar_event_objectives");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "calendar_events");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_event_objectives_event_objective",
                table: "calendar_event_objectives",
                columns: new[] { "calendar_event_id", "objective_id" },
                unique: true);
        }
    }
}
