using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkModeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_work_modes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_system_seeded = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_work_modes", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_work_modes_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_legal_entity_id",
                table: "tenant_work_modes",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_id",
                table: "tenant_work_modes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_id_legal_entity_id",
                table: "tenant_work_modes",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_work_modes_tenant_le_active",
                table: "tenant_work_modes",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_work_modes");
        }
    }
}
