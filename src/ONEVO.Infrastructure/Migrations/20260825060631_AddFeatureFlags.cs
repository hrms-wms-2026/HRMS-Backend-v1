using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    default_value = table.Column<bool>(type: "boolean", nullable: false),
                    rollout_percentage = table.Column<int>(type: "integer", nullable: false),
                    module_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    feature_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.key);
                    table.ForeignKey(
                        name: "fk_feature_flags_module_catalog_module_key",
                        column: x => x.module_key,
                        principalTable: "module_catalog",
                        principalColumn: "module_key",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_feature_flags_module_features_feature_key",
                        column: x => x.feature_key,
                        principalTable: "module_features",
                        principalColumn: "feature_key",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "feature_flag_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<bool>(type: "boolean", nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flag_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_feature_flag_overrides_feature_flags_flag_key",
                        column: x => x.flag_key,
                        principalTable: "feature_flags",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feature_flag_overrides_platform_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feature_flag_overrides_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_flag_overrides_flag_key_tenant_id",
                table: "feature_flag_overrides",
                columns: new[] { "flag_key", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_flag_overrides_granted_by_id",
                table: "feature_flag_overrides",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_flag_overrides_tenant_id",
                table: "feature_flag_overrides",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_feature_flags_feature_key",
                table: "feature_flags",
                column: "feature_key");

            migrationBuilder.CreateIndex(
                name: "ix_feature_flags_module_key",
                table: "feature_flags",
                column: "module_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flag_overrides");

            migrationBuilder.DropTable(
                name: "feature_flags");
        }
    }
}
