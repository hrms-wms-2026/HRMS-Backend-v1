using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentHeadPositionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "head_position_id",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_head_position_id",
                table: "departments",
                column: "head_position_id");

            migrationBuilder.AddForeignKey(
                name: "fk_departments_positions_head_position_id",
                table: "departments",
                column: "head_position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_departments_positions_head_position_id",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_head_position_id",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "head_position_id",
                table: "departments");
        }
    }
}
