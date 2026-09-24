using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformUserIdToPlatformUserInvite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "platform_user_id",
                table: "platform_user_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_platform_user_id",
                table: "platform_user_invites",
                column: "platform_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites",
                column: "platform_user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_platform_user_invites_platform_users_platform_user_id",
                table: "platform_user_invites");

            migrationBuilder.DropIndex(
                name: "ix_platform_user_invites_platform_user_id",
                table: "platform_user_invites");

            migrationBuilder.DropColumn(
                name: "platform_user_id",
                table: "platform_user_invites");
        }
    }
}
