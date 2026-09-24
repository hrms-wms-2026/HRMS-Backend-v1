using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringCheckIn : Migration
    {
        private static readonly string[] TenantTables =
        [
            "employee_check_ins",
            "monitoring_face_scans"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_face_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_in_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_face_scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    location_accuracy = table.Column<double>(type: "double precision", nullable: true),
                    location_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    device_serial_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    face_scan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_check_ins_monitoring_face_scans_face_scan_id",
                        column: x => x.face_scan_id,
                        principalTable: "monitoring_face_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_face_scan_id",
                table: "employee_check_ins",
                column: "face_scan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_tenant_id_device_registration_id",
                table: "employee_check_ins",
                columns: new[] { "tenant_id", "device_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_check_ins_tenant_id_user_id_checked_in_at",
                table: "employee_check_ins",
                columns: new[] { "tenant_id", "user_id", "checked_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_face_scans_storage_key",
                table: "monitoring_face_scans",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_face_scans_tenant_id_check_in_id",
                table: "monitoring_face_scans",
                columns: new[] { "tenant_id", "check_in_id" },
                unique: true);

            // PostgreSQL RLS — tenant isolation on both check-in tables
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
                name: "employee_check_ins");

            migrationBuilder.DropTable(
                name: "monitoring_face_scans");
        }
    }
}
