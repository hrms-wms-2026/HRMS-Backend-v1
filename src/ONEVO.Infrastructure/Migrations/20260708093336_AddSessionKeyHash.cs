using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionKeyHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key_hash",
                table: "sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "key_hash",
                table: "platform_admin_sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE sessions SET key_hash = id::text;");
            migrationBuilder.Sql("UPDATE platform_admin_sessions SET key_hash = id::text;");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_key_hash",
                table: "sessions",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_sessions_key_hash",
                table: "platform_admin_sessions",
                column: "key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_key_hash",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "ix_platform_admin_sessions_key_hash",
                table: "platform_admin_sessions");

            migrationBuilder.DropColumn(
                name: "key_hash",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "key_hash",
                table: "platform_admin_sessions");
        }
    }
}
