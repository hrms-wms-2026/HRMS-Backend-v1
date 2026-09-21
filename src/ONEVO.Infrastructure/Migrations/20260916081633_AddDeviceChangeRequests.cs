using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceChangeRequests : Migration
    {
        // TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage parses
        // this exact `TenantTables = [...]` shape to know what's covered - see
        // AddLocationChangeRequestsRlsPolicyCoverage for the same convention. Adding coverage in
        // this same migration (rather than a follow-up) avoids the gap location_change_requests hit.
        private static readonly string[] TenantTables =
        [
            "device_change_requests"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_device_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_device_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    new_device_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    new_device_os = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_change_requests_tray_device_registrations_current_de",
                        column: x => x.current_device_registration_id,
                        principalTable: "tray_device_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_device_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_current_device_registration_id",
                table: "device_change_requests",
                column: "current_device_registration_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_reviewed_by_id",
                table: "device_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_change_requests_tenant_status",
                table: "device_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_device_change_requests_pending_employee",
                table: "device_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'pending'");

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

            migrationBuilder.DropTable(
                name: "device_change_requests");
        }
    }
}
