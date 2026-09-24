using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionChangeApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "change_reason",
                table: "position_assignments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "change_reason",
                table: "access_grant_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reserved_position_assignment_id",
                table: "access_grant_requests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "change_reason",
                table: "position_assignments");

            migrationBuilder.DropColumn(
                name: "change_reason",
                table: "access_grant_requests");

            migrationBuilder.DropColumn(
                name: "reserved_position_assignment_id",
                table: "access_grant_requests");
        }
    }
}
