using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
        public partial class AddTaskTimeTrackingAndEditHistory : Migration
    {
        private static readonly string[] TenantTables =
        [
            "task_edit_logs", "task_status_change_logs", "task_clocking_sessions", "task_percentage_logs"
        ];


        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_clocking_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clock_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_clocking_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_clocking_sessions_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_edit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    edit_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_values_json = table.Column<string>(type: "jsonb", nullable: false),
                    new_values_json = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_edit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_edit_logs_task_edit_requests_edit_request_id",
                        column: x => x.edit_request_id,
                        principalTable: "task_edit_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_edit_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_status_change_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_status_change_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_task_statuses_from_status_id",
                        column: x => x.from_status_id,
                        principalTable: "task_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_task_statuses_to_status_id",
                        column: x => x.to_status_id,
                        principalTable: "task_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_status_change_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_percentage_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_percent = table.Column<int>(type: "integer", nullable: false),
                    new_percent = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clocking_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_percentage_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_percentage_logs_task_clocking_sessions_clocking_sessio",
                        column: x => x.clocking_session_id,
                        principalTable: "task_clocking_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_percentage_logs_work_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_one_open_per_task",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "task_id" },
                unique: true,
                filter: "clock_out_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_task_id",
                table: "task_clocking_sessions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_clocking_sessions_tenant_id_task_id_clock_in_at",
                table: "task_clocking_sessions",
                columns: new[] { "tenant_id", "task_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_edit_request_id",
                table: "task_edit_logs",
                column: "edit_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_task_id",
                table: "task_edit_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_edit_logs_tenant_id_task_id_changed_at",
                table: "task_edit_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_clocking_session_id",
                table: "task_percentage_logs",
                column: "clocking_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_task_id",
                table: "task_percentage_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_percentage_logs_tenant_id_task_id_changed_at",
                table: "task_percentage_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_from_status_id",
                table: "task_status_change_logs",
                column: "from_status_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_task_id",
                table: "task_status_change_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_tenant_id_task_id_changed_at",
                table: "task_status_change_logs",
                columns: new[] { "tenant_id", "task_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_status_change_logs_to_status_id",
                table: "task_status_change_logs",
                column: "to_status_id");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                ");
            }
        }

        /// <inheritdoc />
                protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(

                name: "task_edit_logs");

            migrationBuilder.DropTable(
                name: "task_percentage_logs");

            migrationBuilder.DropTable(
                name: "task_status_change_logs");

            migrationBuilder.DropTable(
                name: "task_clocking_sessions");
        }
    }
}
