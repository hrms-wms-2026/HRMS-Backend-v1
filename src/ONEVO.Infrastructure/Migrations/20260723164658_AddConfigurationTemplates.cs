using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuration_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    module_keys_json = table.Column<string>(type: "jsonb", nullable: false),
                    industry_profile_tag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuration_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_configuration_templates_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_configuration_template_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    applied_version = table.Column<int>(type: "integer", nullable: false),
                    applied_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    custom_payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applied_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_configuration_template_applications", x => x.id);
                    table.CheckConstraint("ck_tenant_config_template_apps_status", "status IN ('applied')");
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_configuration_te",
                        column: x => x.configuration_template_id,
                        principalTable: "configuration_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_platform_users_a",
                        column: x => x.applied_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_configuration_template_applications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_created_by_id",
                table: "configuration_templates",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_template_key",
                table: "configuration_templates",
                column: "template_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuration_templates_template_type",
                table: "configuration_templates",
                column: "template_type");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_applied_by_id",
                table: "tenant_configuration_template_applications",
                column: "applied_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_configuration_te",
                table: "tenant_configuration_template_applications",
                column: "configuration_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_configuration_template_applications_tenant_id_applie",
                table: "tenant_configuration_template_applications",
                columns: new[] { "tenant_id", "applied_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_configuration_template_applications");

            migrationBuilder.DropTable(
                name: "configuration_templates");
        }
    }
}
