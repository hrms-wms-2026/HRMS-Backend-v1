using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRevertColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reverted_at",
                table: "wm_approval_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reverted_by_employee_id",
                table: "wm_approval_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "undo_state_json",
                table: "wm_approval_requests",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reverted_at",
                table: "wm_approval_requests");

            migrationBuilder.DropColumn(
                name: "reverted_by_employee_id",
                table: "wm_approval_requests");

            migrationBuilder.DropColumn(
                name: "undo_state_json",
                table: "wm_approval_requests");
        }
    }
}
