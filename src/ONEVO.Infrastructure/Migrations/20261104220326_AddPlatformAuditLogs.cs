using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformAuditLogs : Migration
    {
        private const string RuntimeAppRole = "onevo_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_platform_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    target_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_audit_logs", x => x.id);
                });

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

            // Append-only at the database level: ops/postgres/local-bootstrap-roles.sql gives the
            // runtime role SELECT, INSERT, UPDATE, DELETE on every table the migrator creates
            // (ALTER DEFAULT PRIVILEGES). Take UPDATE, DELETE and TRUNCATE back for this table, so
            // the application can only add and read rows. Like the other migrations that grant to
            // onevo_app, this assumes the role already exists (deploy-time bootstrap) and fails
            // loudly if it does not. Production/staging must provision the same restriction if
            // they grant table privileges outside these defaults.
            migrationBuilder.Sql(
                $"""
                REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.platform_audit_logs FROM {RuntimeAppRole};
                GRANT SELECT, INSERT ON TABLE public.platform_audit_logs TO {RuntimeAppRole};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_audit_logs");
        }
    }
}
