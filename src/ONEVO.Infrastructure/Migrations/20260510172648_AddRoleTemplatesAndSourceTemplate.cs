using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleTemplatesAndSourceTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "roles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<Guid>(
                name: "source_template_id",
                table: "roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "role_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    module_keys_json = table.Column<string>(type: "jsonb", nullable: false),
                    permission_codes_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roles_source_template_id",
                table: "roles",
                column: "source_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_source_template_id",
                table: "roles",
                columns: new[] { "tenant_id", "source_template_id" },
                unique: true,
                filter: "source_template_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_templates_name",
                table: "role_templates",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_role_templates_source_template_id",
                table: "roles",
                column: "source_template_id",
                principalTable: "role_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_roles_role_templates_source_template_id",
                table: "roles");

            migrationBuilder.DropTable(
                name: "role_templates");

            migrationBuilder.DropIndex(
                name: "ix_roles_source_template_id",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_roles_tenant_id_source_template_id",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "source_template_id",
                table: "roles");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "roles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
