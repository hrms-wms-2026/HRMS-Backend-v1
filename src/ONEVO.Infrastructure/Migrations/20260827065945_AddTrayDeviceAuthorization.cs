using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrayDeviceAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tray_device_authorizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_fingerprint_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_os = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    poll_violation_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tray_device_authorizations", x => x.id);
                    table.CheckConstraint("ck_tray_device_authorizations_approval_identity", "status NOT IN ('Approved', 'Consumed') OR (approved_tenant_id IS NOT NULL AND approved_user_id IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_tray_device_authorizations_consumed_at", "consumed_at IS NULL OR status = 'Consumed'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_registrations_tenant_id_user_id_is_active_last_",
                table: "tray_device_registrations",
                columns: new[] { "tenant_id", "user_id", "is_active", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_device_code_hash",
                table: "tray_device_authorizations",
                column: "device_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_device_fingerprint_hash_created_",
                table: "tray_device_authorizations",
                columns: new[] { "device_fingerprint_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_status_expires_at",
                table: "tray_device_authorizations",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tray_device_authorizations_user_code_hash_status_expires_at",
                table: "tray_device_authorizations",
                columns: new[] { "user_code_hash", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tray_device_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_tray_device_registrations_tenant_id_user_id_is_active_last_",
                table: "tray_device_registrations");
        }
    }
}
