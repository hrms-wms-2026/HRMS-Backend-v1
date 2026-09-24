using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklistTemplateScopeAndTaskRequiredFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_required",
                table: "employee_checklist_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "checklist_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "position_id",
                table: "checklist_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_legal_entity_id",
                table: "checklist_templates",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_position_id",
                table: "checklist_templates",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_legal_entity_id_template_type",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "legal_entity_id", "template_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_tenant_id_position_id",
                table: "checklist_templates",
                columns: new[] { "tenant_id", "position_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_checklist_templates_legal_entities_legal_entity_id",
                table: "checklist_templates",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_checklist_templates_positions_position_id",
                table: "checklist_templates",
                column: "position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_checklist_templates_legal_entities_legal_entity_id",
                table: "checklist_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_checklist_templates_positions_position_id",
                table: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_checklist_templates_legal_entity_id",
                table: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_checklist_templates_position_id",
                table: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_checklist_templates_tenant_id_legal_entity_id_template_type",
                table: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_checklist_templates_tenant_id_position_id",
                table: "checklist_templates");

            migrationBuilder.DropColumn(
                name: "is_required",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "checklist_templates");

            migrationBuilder.DropColumn(
                name: "position_id",
                table: "checklist_templates");
        }
    }
}
