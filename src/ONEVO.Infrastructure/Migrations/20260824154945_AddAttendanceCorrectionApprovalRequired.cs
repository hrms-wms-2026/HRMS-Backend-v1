using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddAttendanceCorrectionApprovalRequired : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "approval_required",
            table: "attendance_corrections",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            UPDATE attendance_corrections
            SET approval_required = TRUE
            WHERE status IN ('pending', 'rejected', 'cancelled')
               OR reviewed_by_id IS NOT NULL
               OR reviewed_at IS NOT NULL;
            """);

        migrationBuilder.AlterColumn<bool>(
            name: "approval_required",
            table: "attendance_corrections",
            type: "boolean",
            nullable: false,
            oldClrType: typeof(bool),
            oldType: "boolean",
            oldDefaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "approval_required",
            table: "attendance_corrections");
    }
}
