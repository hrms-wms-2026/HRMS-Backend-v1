using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplateVersioningAndRouting : Migration
    {
        // tenant_notification_rules and notification_deliveries are ITenantOwnedEntity tables: standard
        // idempotent tenant_isolation policy via the `TenantTables` + foreach convention that
        // TenantIsolationArchitectureTests.EveryTenantOwnedEntityTable_HasRlsPolicyCoverage parses.
        // notification_templates / notification_template_versions stay global product copy (no RLS).
        private static readonly string[] TenantTables =
        [
            "tenant_notification_rules",
            "notification_deliveries"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "active_version_id",
                table: "notification_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "event_key",
                table: "notification_templates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "locale",
                table: "notification_templates",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "notification_templates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    email_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email_html = table.Column<string>(type: "text", nullable: true),
                    email_text = table.Column<string>(type: "text", nullable: true),
                    action_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    related_entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    in_app_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    in_app_body = table.Column<string>(type: "text", nullable: false),
                    email_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    email_html = table.Column<string>(type: "text", nullable: true),
                    email_css = table.Column<string>(type: "text", nullable: true),
                    email_text = table.Column<string>(type: "text", nullable: true),
                    tray_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    tray_body = table.Column<string>(type: "text", nullable: true),
                    action_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_template_versions_notification_templates_templ",
                        column: x => x.template_id,
                        principalTable: "notification_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_notification_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    enabled_channels = table.Column<int>(type: "integer", nullable: false),
                    recipient_rule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_notification_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_notification_rules_notification_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "notification_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_event_key_locale",
                table: "notification_templates",
                columns: new[] { "event_key", "locale" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_identity",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "event_id", "recipient_user_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_tenant_channel_recipient_status",
                table: "notification_deliveries",
                columns: new[] { "tenant_id", "channel", "recipient_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_one_draft_per_template",
                table: "notification_template_versions",
                column: "template_id",
                unique: true,
                filter: "status = 'draft'");

            migrationBuilder.CreateIndex(
                name: "ix_notification_template_versions_template_id_version",
                table: "notification_template_versions",
                columns: new[] { "template_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_rules_template_id",
                table: "tenant_notification_rules",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_notification_rules_tenant_id_event_key",
                table: "tenant_notification_rules",
                columns: new[] { "tenant_id", "event_key" },
                unique: true);

            // Backfill: every existing template becomes its own catalog event's default and its current copy
            // (including any edits already made in the database) is frozen as published version 1.
            migrationBuilder.Sql(@"
                UPDATE notification_templates SET event_key = code WHERE event_key = '';
                UPDATE notification_templates SET name = code WHERE name = '';
                INSERT INTO notification_template_versions
                    (id, template_id, version, status, revision, in_app_title, in_app_body,
                     email_subject, email_text, action_key, created_at, published_at)
                SELECT gen_random_uuid(), t.id, 1, 'published', 2, t.in_app_title_template, t.in_app_body_template,
                       t.mail_subject_template, t.mail_body_template, 'open_related', now(), now()
                FROM notification_templates t
                WHERE t.active_version_id IS NULL
                  AND NOT EXISTS (SELECT 1 FROM notification_template_versions v WHERE v.template_id = t.id);
                UPDATE notification_templates t SET active_version_id = v.id
                FROM notification_template_versions v
                WHERE v.template_id = t.id AND v.version = 1 AND t.active_version_id IS NULL;
            ");

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
            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_template_versions");

            migrationBuilder.DropTable(
                name: "tenant_notification_rules");

            migrationBuilder.DropIndex(
                name: "ix_notification_templates_event_key_locale",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "active_version_id",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "event_key",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "locale",
                table: "notification_templates");

            migrationBuilder.DropColumn(
                name: "name",
                table: "notification_templates");
        }
    }
}
