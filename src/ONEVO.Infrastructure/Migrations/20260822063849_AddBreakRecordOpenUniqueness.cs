using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddBreakRecordOpenUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ux_break_records_one_open_per_employee",
            table: "break_records",
            columns: new[] { "tenant_id", "employee_id" },
            unique: true,
            filter: "break_end IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_break_records_one_open_per_employee",
            table: "break_records");
    }
}
