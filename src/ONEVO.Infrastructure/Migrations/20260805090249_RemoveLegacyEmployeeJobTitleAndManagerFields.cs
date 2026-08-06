using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyEmployeeJobTitleAndManagerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employees_employees_manager_id",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_manager_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "job_title_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "manager_id",
                table: "employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "job_title_id",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_id",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_manager_id",
                table: "employees",
                column: "manager_id");

            migrationBuilder.AddForeignKey(
                name: "fk_employees_employees_manager_id",
                table: "employees",
                column: "manager_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
