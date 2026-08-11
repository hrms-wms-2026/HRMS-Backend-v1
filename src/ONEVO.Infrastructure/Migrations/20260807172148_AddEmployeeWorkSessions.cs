using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeWorkSessions : Migration
    {
        private static readonly string[] TenantTables = ["employee_work_sessions"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_work_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clock_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accumulated_break_seconds = table.Column<int>(type: "integer", nullable: false),
                    accumulated_work_seconds = table.Column<int>(type: "integer", nullable: false),
                    break_session_count = table.Column<int>(type: "integer", nullable: false),
                    schedule_display = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_work_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_tenant_id_device_registration_id",
                table: "employee_work_sessions",
                columns: new[] { "tenant_id", "device_registration_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_sessions_tenant_id_user_id_clock_in_at",
                table: "employee_work_sessions",
                columns: new[] { "tenant_id", "user_id", "clock_in_at" });

            // PostgreSQL RLS — standard tenant isolation. The submit endpoint is
            // authenticated (TrayDevicePolicy) and explicitly switches into the JWT's
            // tenant via ITenantContextSwitcher before reading/writing (same pattern
            // as MonitoringCheckInController), so this table does not need the
            // 'system'-mode bypass that the anonymous TrayActivation endpoints do.
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
                name: "employee_work_sessions");
        }
    }
}
