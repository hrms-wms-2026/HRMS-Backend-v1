using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    target_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    target_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_audit_logs", x => x.id);
                    table.CheckConstraint("ck_platform_audit_logs_after_json_json", "ISJSON([after_json]) = 1");
                    table.CheckConstraint("ck_platform_audit_logs_before_json_json", "ISJSON([before_json]) = 1");
                    table.CheckConstraint("ck_platform_audit_logs_outcome", "outcome IN ('succeeded', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_logs_action_created_at",
                table: "platform_audit_logs",
                columns: new[] { "action", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_logs_actor_platform_user_id_created_at",
                table: "platform_audit_logs",
                columns: new[] { "actor_platform_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_logs_created_at",
                table: "platform_audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_logs_target_tenant_id_created_at",
                table: "platform_audit_logs",
                columns: new[] { "target_tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_logs_target_type_target_id",
                table: "platform_audit_logs",
                columns: new[] { "target_type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_audit_logs");
        }
    }
}
