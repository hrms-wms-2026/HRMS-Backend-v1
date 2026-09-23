using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivitySnapshotDedupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A duplicate tray-snapshot batch (retry/resend) could already have inserted the same
            // (tenant_id, agent_device_id, captured_at) twice before this constraint existed, which
            // would make CreateIndex below fail. Keep the earliest row per key and drop the rest.
            migrationBuilder.Sql("""
                DELETE FROM activity_snapshots a
                USING activity_snapshots b
                WHERE a.tenant_id = b.tenant_id
                  AND a.agent_device_id = b.agent_device_id
                  AND a.captured_at = b.captured_at
                  AND (a.created_at, a.id) > (b.created_at, b.id);
                """);

            migrationBuilder.DropIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots");

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots");

            migrationBuilder.CreateIndex(
                name: "ix_activity_snapshots_tenant_device_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });
        }
    }
}
