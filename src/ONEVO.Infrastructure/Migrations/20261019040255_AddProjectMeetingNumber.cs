using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMeetingNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_meeting_number",
                table: "projects",
                type: "bigint",
                nullable: false,
                // Existing projects start numbering meetings at 1, like a new project (Project.NextMeetingNumber = 1).
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_meeting_number",
                table: "projects");
        }
    }
}
