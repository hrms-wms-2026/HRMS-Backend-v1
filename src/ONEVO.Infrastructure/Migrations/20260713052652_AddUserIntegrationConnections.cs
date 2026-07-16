using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIntegrationConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_integration_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    integration_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    access_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    refresh_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scopes_granted = table.Column<string[]>(type: "text[]", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disconnected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_integration_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_integration_connections_integration_catalog_integratio",
                        column: x => x.integration_key,
                        principalTable: "integration_catalog",
                        principalColumn: "integration_key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_integration_connections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_integration_connections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_integration_key",
                table: "user_integration_connections",
                column: "integration_key");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_status",
                table: "user_integration_connections",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_integration_key",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "integration_key" });

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_user_id",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_tenant_id_user_id_integration_",
                table: "user_integration_connections",
                columns: new[] { "tenant_id", "user_id", "integration_key" },
                unique: true,
                filter: "disconnected_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_integration_connections_user_id",
                table: "user_integration_connections",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_integration_connections");
        }
    }
}
