using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntityBreakDurationMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "break_duration_minutes",
                table: "legal_entities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_break_duration_minutes",
                table: "legal_entities",
                sql: "break_duration_minutes IS NULL OR break_duration_minutes >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_break_duration_minutes",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "break_duration_minutes",
                table: "legal_entities");
        }
    }
}
