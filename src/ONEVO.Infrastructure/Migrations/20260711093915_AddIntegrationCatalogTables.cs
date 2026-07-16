using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationCatalogTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_platform_o_auth_apps_provider",
                table: "platform_oauth_apps",
                column: "provider");

            migrationBuilder.CreateTable(
                name: "integration_catalog",
                columns: table => new
                {
                    integration_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    connection_scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    onevo_app_provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_catalog", x => x.integration_key);
                    table.ForeignKey(
                        name: "fk_integration_catalog_platform_o_auth_apps_onevo_app_provider",
                        column: x => x.onevo_app_provider,
                        principalTable: "platform_oauth_apps",
                        principalColumn: "provider",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_integration_catalog_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "module_integration_links",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    integration_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    linked_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_integration_links", x => new { x.module_key, x.integration_key });
                    table.ForeignKey(
                        name: "fk_module_integration_links_integration_catalog_integration_key",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_integration_links_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_integration_links_platform_users_linked_by_id",
                        column: x => x.linked_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_integration_catalog_created_by_id",
                table: "integration_catalog",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_integration_catalog_onevo_app_provider",
                table: "integration_catalog",
                column: "onevo_app_provider");

            migrationBuilder.CreateIndex(
                name: "ix_module_integration_links_integration_key",
                table: "module_integration_links",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_module_integration_links_linked_by_id",
                table: "module_integration_links",
                column: "linked_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "module_integration_links");

            migrationBuilder.DropTable(
                name: "integration_catalog");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_platform_o_auth_apps_provider",
                table: "platform_oauth_apps");
        }
    }
}
