using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingAndResignedEmploymentStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "employment_statuses",
                columns: ["id", "code", "label"],
                values: new object[,]
                {
                    { 5, "offboarding", "Offboarding" },
                    { 6, "resigned", "Resigned" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "employment_statuses", keyColumn: "id", keyValues: new object[] { 5, 6 });
        }
    }
}
