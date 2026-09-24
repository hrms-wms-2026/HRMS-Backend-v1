using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringTrayActivation : Migration
    {
        private static readonly string[] TenantTables =
        [
            "tray_activation_codes",
            "tray_device_registrations",
            "tray_device_refresh_tokens"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // tray_activation_codes
            // Stores one-time 10-minute activation codes generated from the web portal.
            // Each code is consumed once by the tray app to exchange for a JWT.
            migrationBuilder.CreateTable(
                name: "tray_activation_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_activation_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_code_hash",
                table: "tray_activation_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_activation_codes_user_id_tenant_id_created_at",
                table: "tray_activation_codes",
                columns: new[] { "user_id", "tenant_id", "created_at" });

            // tray_device_registrations
            // Represents a registered desktop/tray app device for a tenant employee.
            migrationBuilder.CreateTable(
                name: "tray_device_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_os = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_fingerprint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_registrations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_tenant_id_user_id_is_active",
                table: "tray_device_registrations",
                columns: new[] { "tenant_id", "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_device_fingerprint",
                table: "tray_device_registrations",
                column: "device_fingerprint");

            // tray_device_refresh_tokens
            // Long-lived refresh tokens (90 days) stored as hashes. Rotated on every use.
            migrationBuilder.CreateTable(
                name: "tray_device_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_refresh_tokens_token_hash",
                table: "tray_device_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_refresh_tokens_device_registration_id_is_revoked",
                table: "tray_device_refresh_tokens",
                columns: new[] { "device_registration_id", "is_revoked" });

            // PostgreSQL RLS — tenant isolation on all three tables
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(name: "tray_device_refresh_tokens");
            migrationBuilder.DropTable(name: "tray_device_registrations");
            migrationBuilder.DropTable(name: "tray_activation_codes");
        }
    }
}
