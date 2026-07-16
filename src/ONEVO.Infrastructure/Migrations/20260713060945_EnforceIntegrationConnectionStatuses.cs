using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceIntegrationConnectionStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_integration_credentials_status",
                table: "tenant_integration_credentials",
                sql: "status IN ('connected', 'error', 'expired', 'disconnected', 'disabled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_integration_connections_status",
                table: "user_integration_connections",
                sql: "status IN ('connected', 'error', 'expired', 'disconnected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_integration_credentials_status",
                table: "tenant_integration_credentials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_integration_connections_status",
                table: "user_integration_connections");
        }
    }
}
