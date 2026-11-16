using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditTablesAppendOnlyForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_platform_users_actor_admin_user_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_subscription_invoices_invoice_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_tenants_tenant_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_auth_events_platform_users_user_id",
                table: "platform_auth_events");

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_platform_users_actor_admin_user_id",
                table: "billing_audit_logs",
                column: "actor_admin_user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_subscription_invoices_invoice_id",
                table: "billing_audit_logs",
                column: "invoice_id",
                principalTable: "subscription_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_tenants_tenant_id",
                table: "billing_audit_logs",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_auth_events_platform_users_user_id",
                table: "platform_auth_events",
                column: "user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_platform_users_actor_admin_user_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_subscription_invoices_invoice_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_billing_audit_logs_tenants_tenant_id",
                table: "billing_audit_logs");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_auth_events_platform_users_user_id",
                table: "platform_auth_events");

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_platform_users_actor_admin_user_id",
                table: "billing_audit_logs",
                column: "actor_admin_user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_subscription_invoices_invoice_id",
                table: "billing_audit_logs",
                column: "invoice_id",
                principalTable: "subscription_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_billing_audit_logs_tenants_tenant_id",
                table: "billing_audit_logs",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_auth_events_platform_users_user_id",
                table: "platform_auth_events",
                column: "user_id",
                principalTable: "platform_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
