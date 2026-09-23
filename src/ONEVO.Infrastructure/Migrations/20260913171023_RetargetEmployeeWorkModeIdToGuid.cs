using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetargetEmployeeWorkModeIdToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_work_modes_work_mode_id",
                table: "onboarding_drafts");

            migrationBuilder.DropTable(
                name: "work_modes");

            // onboarding_drafts.work_mode_id holds transient WIP data (in-progress drafts, not a
            // historical record) — safe to drop and re-add as a fresh Guid column.
            migrationBuilder.DropColumn(
                name: "work_mode_id",
                table: "onboarding_drafts");

            migrationBuilder.AddColumn<Guid>(
                name: "work_mode_id",
                table: "onboarding_drafts",
                type: "uuid",
                nullable: true);

            // employees.work_mode_id is a historical record — Postgres cannot implicitly cast
            // integer to uuid, so rename the old int column instead of altering it in place, then
            // add the new Guid column alongside it.
            migrationBuilder.RenameColumn(
                name: "work_mode_id",
                table: "employees",
                newName: "legacy_work_mode_id");

            migrationBuilder.AddColumn<Guid>(
                name: "work_mode_id",
                table: "employees",
                type: "uuid",
                nullable: true);

            // bulk_onboarding_batches/rows hold transient WIP data — safe to drop and re-add.
            migrationBuilder.DropColumn(
                name: "default_work_mode_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.AddColumn<Guid>(
                name: "default_work_mode_id",
                table: "bulk_onboarding_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_work_mode_id",
                table: "employees",
                column: "work_mode_id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_time_attendance_work_modes_work_mode_id",
                table: "employees",
                column: "work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_time_attendance_work_modes_work_mode_id",
                table: "onboarding_drafts",
                column: "work_mode_id",
                principalTable: "tenant_work_modes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employees_time_attendance_work_modes_work_mode_id",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_onboarding_drafts_time_attendance_work_modes_work_mode_id",
                table: "onboarding_drafts");

            migrationBuilder.DropIndex(
                name: "ix_employees_work_mode_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_mode_id",
                table: "employees");

            migrationBuilder.RenameColumn(
                name: "legacy_work_mode_id",
                table: "employees",
                newName: "work_mode_id");

            migrationBuilder.DropColumn(
                name: "work_mode_id",
                table: "onboarding_drafts");

            migrationBuilder.AddColumn<int>(
                name: "work_mode_id",
                table: "onboarding_drafts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropColumn(
                name: "default_work_mode_id",
                table: "bulk_onboarding_batches");

            migrationBuilder.AddColumn<int>(
                name: "default_work_mode_id",
                table: "bulk_onboarding_batches",
                type: "integer",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows");

            migrationBuilder.AddColumn<int>(
                name: "resolved_work_mode_id",
                table: "bulk_onboarding_batch_rows",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_modes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_modes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_modes_code",
                table: "work_modes",
                column: "code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_onboarding_drafts_work_modes_work_mode_id",
                table: "onboarding_drafts",
                column: "work_mode_id",
                principalTable: "work_modes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
