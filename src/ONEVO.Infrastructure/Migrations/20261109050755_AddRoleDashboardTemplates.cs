using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleDashboardTemplates : Migration
    {
        private static readonly string[] TenantTables = ["dashboard_templates", "dashboard_template_widgets", "dashboard_template_widget_layouts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "applied_no_role",
                table: "user_dashboards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "applied_role_ids",
                table: "user_dashboards",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.CreateTable(
                name: "dashboard_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_no_role_template = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_templates", x => x.id);
                    table.CheckConstraint("ck_dashboard_templates_no_role_flag", "(is_no_role_template AND role_id IS NULL) OR (NOT is_no_role_template AND role_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_dashboard_templates_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_template_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_template_widgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_dashboard_template_widgets_dashboard_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "dashboard_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_template_widget_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_widget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    breakpoint = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    w = table.Column<int>(type: "integer", nullable: false),
                    h = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_template_widget_layouts", x => x.id);
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_breakpoint", "breakpoint IN ('desktop', 'tablet', 'mobile')");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_h", "h > 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_w", "w > 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_x", "x >= 0");
                    table.CheckConstraint("ck_dashboard_template_widget_layouts_y", "y >= 0");
                    table.ForeignKey(
                        name: "fk_dashboard_template_widget_layouts_dashboard_template_widget",
                        column: x => x.template_widget_id,
                        principalTable: "dashboard_template_widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_template_widget_layouts_template_widget_id_breakpoint",
                table: "dashboard_template_widget_layouts",
                columns: new[] { "template_widget_id", "breakpoint" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_template_widgets_template_id_widget_key",
                table: "dashboard_template_widgets",
                columns: new[] { "template_id", "widget_key" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_templates_role_id",
                table: "dashboard_templates",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_templates_tenant_id_dashboard_key_no_role",
                table: "dashboard_templates",
                columns: new[] { "tenant_id", "dashboard_key" },
                unique: true,
                filter: "is_no_role_template = true AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ux_dashboard_templates_tenant_id_role_id_dashboard_key",
                table: "dashboard_templates",
                columns: new[] { "tenant_id", "role_id", "dashboard_key" },
                unique: true,
                filter: "role_id IS NOT NULL AND is_deleted = false");

            // Tenant isolation, same policy as the other tenant-owned tables.
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

            // Applied-roles backfill (spec 8.3): existing dashboards count their users' current active roles, and the
            // No-role template, as already applied. Otherwise the first auto-sync would push the seeded templates into
            // dashboards users already customized and re-add widgets they removed. Runs as the migration owner, so
            // RLS on user_dashboards / user_roles does not hide rows.
            migrationBuilder.Sql(@"
                UPDATE user_dashboards d
                SET applied_no_role = true,
                    applied_role_ids = COALESCE((
                        SELECT jsonb_agg(ur.role_id ORDER BY ur.assigned_at, ur.role_id)
                        FROM user_roles ur
                        JOIN roles r ON r.id = ur.role_id AND r.is_deleted = false
                        WHERE ur.user_id = d.user_id
                          AND ur.tenant_id = d.tenant_id
                          AND (ur.expires_at IS NULL OR ur.expires_at > now())), '[]'::jsonb)
                WHERE d.is_deleted = false;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "dashboard_template_widget_layouts");

            migrationBuilder.DropTable(
                name: "dashboard_template_widgets");

            migrationBuilder.DropTable(
                name: "dashboard_templates");

            migrationBuilder.DropColumn(
                name: "applied_no_role",
                table: "user_dashboards");

            migrationBuilder.DropColumn(
                name: "applied_role_ids",
                table: "user_dashboards");
        }
    }
}
