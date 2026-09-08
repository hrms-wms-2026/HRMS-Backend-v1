using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The original 20260817180000_AddNotifications migration never shipped its paired
    /// Designer.cs snapshot file, so EF's migration history never recognized or applied it -
    /// leaving monitoring_notifications missing from every environment that only ran
    /// `dotnet ef database update` (the model snapshot already assumed the table existed,
    /// so `migrations add` produced an empty diff instead of surfacing the gap). This
    /// migration re-applies that migration's Up/Down verbatim under a fresh id so EF can
    /// actually run it.
    /// </remarks>
    public partial class FixMissingMonitoringNotificationsTable : Migration
    {
        private static readonly string[] TenantTables = ["monitoring_notifications"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_to_tray_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_notifications_tenant_employee_created",
                table: "monitoring_notifications",
                columns: new[] { "tenant_id", "employee_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_notifications_tenant_employee_type_created",
                table: "monitoring_notifications",
                columns: new[] { "tenant_id", "employee_id", "type", "created_at" },
                descending: new[] { false, false, false, true });

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
                name: "monitoring_notifications");
        }
    }
}
