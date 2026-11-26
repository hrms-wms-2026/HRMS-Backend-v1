using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Improvements task 35, option C (DEC-089). The tray sends activity, app usage and device state
    /// from three collectors with their own timers and timestamps, so the three tables stay apart.
    /// <list type="bullet">
    /// <item>Each table is clustered on (tenant_id, captured_at, id) instead of the random GUID id,
    /// so new captures append at the end and a date range reads neighbouring pages. The primary key
    /// on id stays, nonclustered. The evidence-asset foreign key to activity_snapshots is dropped and
    /// re-created around the primary key change, as SQL Server requires.</item>
    /// <item>(tenant_id, agent_device_id, captured_at) becomes unique on app_usage_snapshots and
    /// device_state_snapshots, as it already is on activity_snapshots: the database refuses the
    /// second row of a retried upload. Rows that already break this rule are retry duplicates; the
    /// earliest row (by created_at, then id) is kept and the rest are deleted first.</item>
    /// <item>The clean-up runs in the 'admin' tenant context mode, because the tables are under
    /// tenant RLS for every login.</item>
    /// </list>
    /// </summary>
    public partial class ClusterSnapshotsByCaptureTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (
                        PARTITION BY tenant_id, agent_device_id, captured_at
                        ORDER BY created_at, id) AS rn
                    FROM dbo.app_usage_snapshots
                )
                DELETE FROM ranked WHERE rn > 1;

                WITH ranked AS (
                    SELECT ROW_NUMBER() OVER (
                        PARTITION BY tenant_id, agent_device_id, captured_at
                        ORDER BY created_at, id) AS rn
                    FROM dbo.device_state_snapshots
                )
                DELETE FROM ranked WHERE rn > 1;

                EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_device_state_snapshots",
                table: "device_state_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_app_usage_snapshots",
                table: "app_usage_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_app_usage_snapshots_tenant_device_captured",
                table: "app_usage_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_activity_snapshots",
                table: "activity_snapshots");

            migrationBuilder.AddPrimaryKey(
                name: "pk_device_state_snapshots",
                table: "device_state_snapshots",
                column: "id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "pk_app_usage_snapshots",
                table: "app_usage_snapshots",
                column: "id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.AddPrimaryKey(
                name: "pk_activity_snapshots",
                table: "activity_snapshots",
                column: "id")
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.CreateIndex(
                name: "cx_device_state_snapshots_tenant_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "captured_at", "id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "cx_app_usage_snapshots_tenant_captured",
                table: "app_usage_snapshots",
                columns: new[] { "tenant_id", "captured_at", "id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_tenant_device_captured",
                table: "app_usage_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "cx_activity_snapshots_tenant_captured",
                table: "activity_snapshots",
                columns: new[] { "tenant_id", "captured_at", "id" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id",
                table: "monitoring_evidence_assets",
                column: "activity_snapshot_id",
                principalTable: "activity_snapshots",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id",
                table: "monitoring_evidence_assets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_device_state_snapshots",
                table: "device_state_snapshots");

            migrationBuilder.DropIndex(
                name: "cx_device_state_snapshots_tenant_captured",
                table: "device_state_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_app_usage_snapshots",
                table: "app_usage_snapshots");

            migrationBuilder.DropIndex(
                name: "cx_app_usage_snapshots_tenant_captured",
                table: "app_usage_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_app_usage_snapshots_tenant_device_captured",
                table: "app_usage_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_activity_snapshots",
                table: "activity_snapshots");

            migrationBuilder.DropIndex(
                name: "cx_activity_snapshots_tenant_captured",
                table: "activity_snapshots");

            migrationBuilder.AddPrimaryKey(
                name: "pk_device_state_snapshots",
                table: "device_state_snapshots",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_app_usage_snapshots",
                table: "app_usage_snapshots",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_activity_snapshots",
                table: "activity_snapshots",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_device_state_snapshots_tenant_device_captured",
                table: "device_state_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_app_usage_snapshots_tenant_device_captured",
                table: "app_usage_snapshots",
                columns: new[] { "tenant_id", "agent_device_id", "captured_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_monitoring_evidence_assets_activity_snapshots_activity_snapshot_id",
                table: "monitoring_evidence_assets",
                column: "activity_snapshot_id",
                principalTable: "activity_snapshots",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
