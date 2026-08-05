using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameLegalEntityFirstDayOfWeekToWeekStartDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_first_day_of_week",
                table: "legal_entities");

            migrationBuilder.RenameColumn(
                name: "first_day_of_week",
                table: "legal_entities",
                newName: "week_start_day");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_week_start_day",
                table: "legal_entities",
                sql: "week_start_day BETWEEN 1 AND 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_week_start_day",
                table: "legal_entities");

            migrationBuilder.RenameColumn(
                name: "week_start_day",
                table: "legal_entities",
                newName: "first_day_of_week");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_first_day_of_week",
                table: "legal_entities",
                sql: "first_day_of_week BETWEEN 1 AND 7");
        }
    }
}
