using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceCorrectionForeignKeyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Corrective, additive migration: these five foreign-key-support indexes were
            // already declared in the EF model (and therefore already recorded in
            // ApplicationDbContextModelSnapshot.cs / the AddAttendanceCorrectionApprovalRequired
            // migration's own Designer.cs) but were never actually created as DDL by the
            // original AddAttendanceCorrections migration, so a real database ends up missing
            // them and no future `dotnet ef migrations add` would ever propose them again. This
            // migration only creates the missing indexes; it does not touch FKs, RLS, or any
            // other column.
            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_employee_id",
                table: "attendance_corrections",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_legal_entity_id",
                table: "attendance_corrections",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_presence_session_id",
                table: "attendance_corrections",
                column: "presence_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_requested_by_id",
                table: "attendance_corrections",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_corrections_reviewed_by_id",
                table: "attendance_corrections",
                column: "reviewed_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_attendance_corrections_employee_id",
                table: "attendance_corrections");

            migrationBuilder.DropIndex(
                name: "ix_attendance_corrections_legal_entity_id",
                table: "attendance_corrections");

            migrationBuilder.DropIndex(
                name: "ix_attendance_corrections_presence_session_id",
                table: "attendance_corrections");

            migrationBuilder.DropIndex(
                name: "ix_attendance_corrections_requested_by_id",
                table: "attendance_corrections");

            migrationBuilder.DropIndex(
                name: "ix_attendance_corrections_reviewed_by_id",
                table: "attendance_corrections");
        }
    }
}
