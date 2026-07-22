using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStatusHistoryForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_changed_by_id",
                table: "tenant_status_histories",
                column: "changed_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_status_histories_platform_users_changed_by_id",
                table: "tenant_status_histories",
                column: "changed_by_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_status_histories_tenants_tenant_id",
                table: "tenant_status_histories",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_status_histories_platform_users_changed_by_id",
                table: "tenant_status_histories");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_status_histories_tenants_tenant_id",
                table: "tenant_status_histories");

            migrationBuilder.DropIndex(
                name: "ix_tenant_status_histories_changed_by_id",
                table: "tenant_status_histories");
        }
    }
}
