using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayCalendarSettings : Migration
    {
        // Issued through the same `TenantTables` + foreach convention as
        // AddCalendarEventsRlsPolicyCoverage: TenantIsolationArchitectureTests
        // .EveryTenantOwnedEntityTable_HasRlsPolicyCoverage only recognises RLS
        // coverage from migrations that declare this literal, so an inline
        // `CREATE POLICY ... ON holiday_calendar_settings` alone would be
        // flagged as uncovered.
        private static readonly string[] TenantTables =
        [
            "holiday_calendar_settings"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holiday_calendar_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    override_country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    holiday_sync_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "nager_holidays"),
                    last_synced_year = table.Column<int>(type: "integer", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendar_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendar_settings_one_per_legal_entity",
                table: "holiday_calendar_settings",
                columns: new[] { "tenant_id", "legal_entity_id" },
                unique: true);

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
                name: "holiday_calendar_settings");
        }
    }
}
