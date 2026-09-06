using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHolidayCalendarSettings : Migration
    {
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

            migrationBuilder.Sql(@"
                ALTER TABLE holiday_calendar_settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE holiday_calendar_settings FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON holiday_calendar_settings;
                CREATE POLICY tenant_isolation ON holiday_calendar_settings
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON holiday_calendar_settings;
                ALTER TABLE holiday_calendar_settings DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "holiday_calendar_settings");
        }
    }
}
