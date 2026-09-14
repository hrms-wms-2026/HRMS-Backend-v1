using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalCalendarSync : Migration
    {
        private static readonly string[] TenantTables = ["external_calendar_connections", "external_calendar_event_links"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_account_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    external_calendar_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_calendar_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    access_token_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    refresh_token_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    scopes = table.Column<string>(type: "jsonb", nullable: false),
                    sync_direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sync_token_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    delta_link_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_successful_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_calendar_connections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_calendar_event_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_calendar_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_calendar_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    external_etag = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sync_direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sync_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_calendar_event_links", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_connections_one_per_user_provider",
                table: "external_calendar_connections",
                columns: new[] { "tenant_id", "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_connections_tenant_id_user_id",
                table: "external_calendar_connections",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_one_per_connection_event",
                table: "external_calendar_event_links",
                columns: new[] { "external_calendar_connection_id", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_tenant_id_calendar_event_id",
                table: "external_calendar_event_links",
                columns: new[] { "tenant_id", "calendar_event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_external_calendar_event_links_tenant_id_connection_id",
                table: "external_calendar_event_links",
                columns: new[] { "tenant_id", "external_calendar_connection_id" });

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
                name: "external_calendar_connections");

            migrationBuilder.DropTable(
                name: "external_calendar_event_links");
        }
    }
}
