using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixActivityRawBufferSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_raw_buffer_tenant_id_agent_id_received_at",
                table: "activity_raw_buffer");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "activity_raw_buffer");

            migrationBuilder.RenameColumn(
                name: "events_json",
                table: "activity_raw_buffer",
                newName: "payload_json");

            migrationBuilder.RenameColumn(
                name: "employee_id",
                table: "activity_raw_buffer",
                newName: "agent_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_tenant_id_agent_device_id_received_at",
                table: "activity_raw_buffer",
                columns: new[] { "tenant_id", "agent_device_id", "received_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_raw_buffer_tenant_id_agent_device_id_received_at",
                table: "activity_raw_buffer");

            migrationBuilder.RenameColumn(
                name: "payload_json",
                table: "activity_raw_buffer",
                newName: "events_json");

            migrationBuilder.RenameColumn(
                name: "agent_device_id",
                table: "activity_raw_buffer",
                newName: "employee_id");

            migrationBuilder.AddColumn<Guid>(
                name: "agent_id",
                table: "activity_raw_buffer",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_activity_raw_buffer_tenant_id_agent_id_received_at",
                table: "activity_raw_buffer",
                columns: new[] { "tenant_id", "agent_id", "received_at" });
        }
    }
}
