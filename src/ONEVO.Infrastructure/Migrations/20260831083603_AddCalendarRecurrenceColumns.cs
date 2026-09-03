using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarRecurrenceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_recurrence_cancelled",
                table: "personal_calendar_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recurrence_original_start",
                table: "personal_calendar_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurrence_parent_id",
                table: "personal_calendar_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_recurrence_parent_id",
                table: "personal_calendar_events",
                column: "recurrence_parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_personal_calendar_events_tenant_id_recurrence_parent_id",
                table: "personal_calendar_events",
                columns: new[] { "tenant_id", "recurrence_parent_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_personal_calendar_events_recurrence_parent_id",
                table: "personal_calendar_events",
                column: "recurrence_parent_id",
                principalTable: "personal_calendar_events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_personal_calendar_events_recurrence_parent_id",
                table: "personal_calendar_events");

            migrationBuilder.DropIndex(
                name: "ix_personal_calendar_events_recurrence_parent_id",
                table: "personal_calendar_events");

            migrationBuilder.DropIndex(
                name: "ix_personal_calendar_events_tenant_id_recurrence_parent_id",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "is_recurrence_cancelled",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "recurrence_original_start",
                table: "personal_calendar_events");

            migrationBuilder.DropColumn(
                name: "recurrence_parent_id",
                table: "personal_calendar_events");
        }
    }
}
