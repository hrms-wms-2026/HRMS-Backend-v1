using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionAccessTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_json",
                table: "legal_entities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "position_access_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_access_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_access_templates_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_access_templates_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_position_id",
                table: "position_access_templates",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_role_id",
                table: "position_access_templates",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_tenant_id",
                table: "position_access_templates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_access_templates_tenant_id_position_id",
                table: "position_access_templates",
                columns: new[] { "tenant_id", "position_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "position_access_templates");

            migrationBuilder.DropColumn(
                name: "address_json",
                table: "legal_entities");
        }
    }
}
