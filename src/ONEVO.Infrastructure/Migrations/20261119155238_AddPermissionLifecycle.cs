using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                table: "permissions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "permission_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    change_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    previous_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_changes", x => x.id);
                    table.CheckConstraint("ck_permission_changes_detail_json", "ISJSON([detail]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_permission_changes_change_key",
                table: "permission_changes",
                column: "change_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "permission_changes");

            migrationBuilder.DropColumn(
                name: "retired_at",
                table: "permissions");
        }
    }
}
