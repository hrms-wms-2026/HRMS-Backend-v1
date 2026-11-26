using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Improvements task 45: tables for admin rate limit overrides and their history. No rows are seeded:
    /// an empty table means "use appsettings". Both tables are under RLS (TenantOrSystem): admin mode
    /// writes them, system mode reads them, and tenant mode never sees a global or plan row.
    /// </summary>
    public partial class AddRateLimitSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_limit_setting_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    subscription_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    limit_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    old_per_minute = table.Column<int>(type: "int", nullable: true),
                    old_burst = table.Column<int>(type: "int", nullable: true),
                    new_per_minute = table.Column<int>(type: "int", nullable: true),
                    new_burst = table.Column<int>(type: "int", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_limit_setting_changes", x => x.id);
                    table.CheckConstraint("ck_rate_limit_setting_changes_has_values", "[old_per_minute] IS NOT NULL OR [new_per_minute] IS NOT NULL");
                    table.CheckConstraint("ck_rate_limit_setting_changes_limit_name", "[limit_name] IN ('Anonymous', 'SignedIn', 'TrayDevice', 'TenantCeiling', 'AdminAnonymous', 'AdminSignedIn')");
                    table.CheckConstraint("ck_rate_limit_setting_changes_reason", "LTRIM([reason]) <> ''");
                    table.CheckConstraint("ck_rate_limit_setting_changes_scope_ids", "([scope] = 'global' AND [subscription_plan_id] IS NULL AND [tenant_id] IS NULL) OR ([scope] = 'plan' AND [subscription_plan_id] IS NOT NULL AND [tenant_id] IS NULL) OR ([scope] = 'tenant' AND [tenant_id] IS NOT NULL AND [subscription_plan_id] IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "rate_limit_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scope = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    subscription_plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    limit_name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    per_minute = table.Column<int>(type: "int", nullable: false),
                    burst = table.Column<int>(type: "int", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_limit_settings", x => x.id);
                    table.CheckConstraint("ck_rate_limit_settings_burst", "([limit_name] IN ('Anonymous', 'AdminAnonymous') AND [burst] IS NULL) OR ([limit_name] NOT IN ('Anonymous', 'AdminAnonymous') AND [burst] IS NOT NULL)");
                    table.CheckConstraint("ck_rate_limit_settings_limit_name", "[limit_name] IN ('Anonymous', 'SignedIn', 'TrayDevice', 'TenantCeiling', 'AdminAnonymous', 'AdminSignedIn')");
                    table.CheckConstraint("ck_rate_limit_settings_platform_limits_global_only", "[scope] = 'global' OR [limit_name] IN ('SignedIn', 'TrayDevice', 'TenantCeiling')");
                    table.CheckConstraint("ck_rate_limit_settings_ranges", "[per_minute] BETWEEN 10 AND 200000 AND ([burst] IS NULL OR [burst] BETWEEN 1 AND 50000)");
                    table.CheckConstraint("ck_rate_limit_settings_scope_ids", "([scope] = 'global' AND [subscription_plan_id] IS NULL AND [tenant_id] IS NULL) OR ([scope] = 'plan' AND [subscription_plan_id] IS NOT NULL AND [tenant_id] IS NULL) OR ([scope] = 'tenant' AND [tenant_id] IS NOT NULL AND [subscription_plan_id] IS NULL)");
                    table.ForeignKey(
                        name: "fk_rate_limit_settings_platform_users_updated_by_platform_user_id",
                        column: x => x.updated_by_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_limit_settings_subscription_plans_subscription_plan_id",
                        column: x => x.subscription_plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rate_limit_settings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rate_limit_setting_changes_changed_at",
                table: "rate_limit_setting_changes",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "ix_rate_limit_setting_changes_tenant_changed_at",
                table: "rate_limit_setting_changes",
                columns: new[] { "tenant_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rate_limit_settings_updated_by_platform_user_id",
                table: "rate_limit_settings",
                column: "updated_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_rate_limit_settings_global",
                table: "rate_limit_settings",
                column: "limit_name",
                unique: true,
                filter: "[scope] = 'global'");

            migrationBuilder.CreateIndex(
                name: "ux_rate_limit_settings_plan",
                table: "rate_limit_settings",
                columns: new[] { "subscription_plan_id", "limit_name" },
                unique: true,
                filter: "[scope] = 'plan' AND [subscription_plan_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rate_limit_settings_tenant",
                table: "rate_limit_settings",
                columns: new[] { "tenant_id", "limit_name" },
                unique: true,
                filter: "[scope] = 'tenant' AND [tenant_id] IS NOT NULL");

            foreach (var (table, predicate) in RateLimitTables)
            {
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, predicate));
            }
        }

        private static readonly (string Table, RlsPredicate Predicate)[] RateLimitTables =
        [
            ("rate_limit_settings", RlsTableRegistry.AddedAfterBaseline["rate_limit_settings"]),
            ("rate_limit_setting_changes", RlsTableRegistry.AddedAfterBaseline["rate_limit_setting_changes"]),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in RateLimitTables)
            {
                migrationBuilder.Sql(RlsSql.DropPolicy(table));
            }

            migrationBuilder.DropTable(
                name: "rate_limit_setting_changes");

            migrationBuilder.DropTable(
                name: "rate_limit_settings");
        }
    }
}
