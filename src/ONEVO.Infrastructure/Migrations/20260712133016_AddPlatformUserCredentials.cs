using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformUserCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_user_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    password_algorithm = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reset_token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    reset_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_credentials", x => x.id);
                    table.CheckConstraint("ck_platform_user_credentials_credential_type", "credential_type IN ('password')");
                    table.CheckConstraint("ck_platform_user_credentials_password_hash", "credential_type <> 'password' OR password_hash IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_platform_user_credentials_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_platform_user_id",
                table: "platform_user_credentials",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_platform_user_id_credential_type",
                table: "platform_user_credentials",
                columns: new[] { "platform_user_id", "credential_type" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_credentials_reset_token_hash",
                table: "platform_user_credentials",
                column: "reset_token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_user_credentials");
        }
    }
}
