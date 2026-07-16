using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOAuthAppsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_oauth_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    app_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    authorization_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    token_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    default_scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_oauth_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_oauth_apps_platform_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_oauth_app_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_oauth_app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_secret_encrypted = table.Column<string>(type: "text", nullable: false),
                    private_key_encrypted = table.Column<string>(type: "text", nullable: true),
                    encryption_key_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    credential_version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    rotated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_oauth_app_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_oauth_apps_platform",
                        column: x => x.platform_oauth_app_id,
                        principalTable: "platform_oauth_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_users_deactivated_b",
                        column: x => x.deactivated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_oauth_app_credentials_platform_users_rotated_by_id",
                        column: x => x.rotated_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_deactivated_by_id",
                table: "platform_oauth_app_credentials",
                column: "deactivated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_platform_oauth_app_id",
                table: "platform_oauth_app_credentials",
                column: "platform_oauth_app_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_app_credentials_rotated_by_id",
                table: "platform_oauth_app_credentials",
                column: "rotated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_apps_provider",
                table: "platform_oauth_apps",
                column: "provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_oauth_apps_updated_by_id",
                table: "platform_oauth_apps",
                column: "updated_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_oauth_app_credentials");

            migrationBuilder.DropTable(
                name: "platform_oauth_apps");
        }
    }
}
