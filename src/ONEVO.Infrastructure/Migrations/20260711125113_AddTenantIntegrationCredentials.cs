using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIntegrationCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_integration_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    access_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scopes_granted = table.Column<string[]>(type: "text[]", nullable: false),
                    external_account_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    connected_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_integration_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_integration_catalog_integrat",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_integration_credentials_users_connected_by_user_id",
                        column: x => x.connected_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_connected_by_user_id",
                table: "tenant_integration_credentials",
                column: "connected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_integration_key",
                table: "tenant_integration_credentials",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_tenant_id_integration_key",
                table: "tenant_integration_credentials",
                columns: new[] { "tenant_id", "integration_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_integration_credentials_tenant_id_status",
                table: "tenant_integration_credentials",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_integration_credentials");
        }
    }
}
