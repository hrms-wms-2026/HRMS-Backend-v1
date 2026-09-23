using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingExecutionTables : Migration
    {
        private static readonly string[] TenantTables = ["offboarding_records", "offboarding_task_bypass_requests"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offboarding_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    knowledge_risk_level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rehire_eligibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    checklist_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exit_interview_notes = table.Column<string>(type: "text", nullable: true),
                    penalties_json = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    initiated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_employment_status_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_offboarding_records_checklist_templates_checklist_template_",
                        column: x => x.checklist_template_id,
                        principalTable: "checklist_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offboarding_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offboarding_task_bypass_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_checklist_task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offboarding_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bypass_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    penalty_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    prior_task_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offboarding_task_bypass_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_offboarding_task_bypass_requests_employee_checklist_tasks_e",
                        column: x => x.employee_checklist_task_id,
                        principalTable: "employee_checklist_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_offboarding_task_bypass_requests_offboarding_records_offboa",
                        column: x => x.offboarding_record_id,
                        principalTable: "offboarding_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_checklist_template_id",
                table: "offboarding_records",
                column: "checklist_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_employee_id",
                table: "offboarding_records",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_tenant_id_employee_id",
                table: "offboarding_records",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status IN ('initiated','in_progress')");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_records_tenant_id_employee_id_status",
                table: "offboarding_records",
                columns: new[] { "tenant_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_employee_checklist_task_id",
                table: "offboarding_task_bypass_requests",
                column: "employee_checklist_task_id",
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_offboarding_record_id",
                table: "offboarding_task_bypass_requests",
                column: "offboarding_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_offboarding_task_bypass_requests_tenant_id_approver_id_stat",
                table: "offboarding_task_bypass_requests",
                columns: new[] { "tenant_id", "approver_id", "status" });

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
                name: "offboarding_task_bypass_requests");

            migrationBuilder.DropTable(
                name: "offboarding_records");
        }
    }
}
