using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingFieldsToEmployeeChecklistTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bypass_penalty_description",
                table: "employee_checklist_tasks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "employee_checklist_tasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_bypassable",
                table: "employee_checklist_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "offboarding_record_id",
                table: "employee_checklist_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_offboarding_record_id",
                table: "employee_checklist_tasks",
                column: "offboarding_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_checklist_tasks_tenant_id_offboarding_record_id",
                table: "employee_checklist_tasks",
                columns: new[] { "tenant_id", "offboarding_record_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_employee_checklist_tasks_offboarding_records_offboarding_re",
                table: "employee_checklist_tasks",
                column: "offboarding_record_id",
                principalTable: "offboarding_records",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employee_checklist_tasks_offboarding_records_offboarding_re",
                table: "employee_checklist_tasks");

            migrationBuilder.DropIndex(
                name: "ix_employee_checklist_tasks_offboarding_record_id",
                table: "employee_checklist_tasks");

            migrationBuilder.DropIndex(
                name: "ix_employee_checklist_tasks_tenant_id_offboarding_record_id",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "bypass_penalty_description",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "category",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "is_bypassable",
                table: "employee_checklist_tasks");

            migrationBuilder.DropColumn(
                name: "offboarding_record_id",
                table: "employee_checklist_tasks");
        }
    }
}
