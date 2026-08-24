using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveManagementSchema : Migration
    {
        private static readonly string[] TenantTables =
        [
            "leave_types",
            "leave_policies",
            "leave_policy_leave_types",
            "leave_policy_blackout_periods",
            "leave_policy_legal_entities",
            "leave_entitlements",
            "leave_requests",
            "leave_request_approvers",
            "leave_request_documents",
            "leave_approval_delegates",
            "leave_balance_audits"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leave_approval_delegates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_approval_delegates", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_approval_delegates_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_approval_delegates_employees_delegate_employee_id",
                        column: x => x.delegate_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    job_level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    accrual_start = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accrual_after_n_months = table.Column<int>(type: "integer", nullable: true),
                    proration_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    probation_restriction = table.Column<bool>(type: "boolean", nullable: false),
                    minimum_tenure_months = table.Column<int>(type: "integer", nullable: false),
                    first_year_reduced_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    minimum_notice_days = table.Column<int>(type: "integer", nullable: false),
                    max_consecutive_days = table.Column<int>(type: "integer", nullable: true),
                    min_days_per_request = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    max_team_absence_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    approval_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    requires_document = table.Column<bool>(type: "boolean", nullable: false),
                    document_required_after_days = table.Column<int>(type: "integer", nullable: true),
                    accepted_document_types = table.Column<string[]>(type: "text[]", nullable: false),
                    max_consecutive_days = table.Column<int>(type: "integer", nullable: true),
                    default_days_per_year = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    carry_forward_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    max_carry_forward_days = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "integer", nullable: true),
                    pro_rata_for_new_joiners = table.Column<bool>(type: "boolean", nullable: false),
                    applicable_gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    minimum_notice_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_blackout_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_blackout_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_blackout_periods_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_legal_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_legal_entities", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_legal_entities_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_policy_legal_entities_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    total_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    used_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    pending_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    carried_forward_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    manual_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_entitlements_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_entitlements_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    annual_entitlement_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    carry_forward_max_days = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_leave_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_leave_types_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_policy_leave_types_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    half_day_period = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    total_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    paid_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    unpaid_days = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    conflict_snapshot_json = table.Column<string>(type: "jsonb", nullable: true),
                    notice_period_missed = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_on_behalf_of_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    partial_cancel_effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_requests_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_balance_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    days_changed = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    related_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_balance_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_leave_requests_related_request_id",
                        column: x => x.related_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_balance_audits_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_request_approvers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delegated_from_approver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_approvers", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_approvers_employees_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_request_approvers_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "leave_request_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_record_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_request_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_request_documents_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leave_request_documents_leave_requests_leave_request_id",
                        column: x => x.leave_request_id,
                        principalTable: "leave_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_approver_employee_id",
                table: "leave_approval_delegates",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_delegate_employee_id",
                table: "leave_approval_delegates",
                column: "delegate_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_approval_delegates_tenant_approver",
                table: "leave_approval_delegates",
                columns: new[] { "tenant_id", "approver_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_employee_id",
                table: "leave_balance_audits",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_leave_type_id",
                table: "leave_balance_audits",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_related_request_id",
                table: "leave_balance_audits",
                column: "related_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_tenant_employee_type",
                table: "leave_balance_audits",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_employee_id",
                table: "leave_entitlements",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_leave_type_id",
                table: "leave_entitlements",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_entitlements_tenant_employee_type_year",
                table: "leave_entitlements",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_tenant_id",
                table: "leave_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_blackout_periods_leave_policy_id",
                table: "leave_policy_blackout_periods",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_blackout_periods_tenant_policy",
                table: "leave_policy_blackout_periods",
                columns: new[] { "tenant_id", "leave_policy_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_leave_types_leave_policy_id",
                table: "leave_policy_leave_types",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_leave_types_leave_type_id",
                table: "leave_policy_leave_types",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_leave_types_tenant_policy_type",
                table: "leave_policy_leave_types",
                columns: new[] { "tenant_id", "leave_policy_id", "leave_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_legal_entities_leave_policy_id",
                table: "leave_policy_legal_entities",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_legal_entities_legal_entity_id",
                table: "leave_policy_legal_entities",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_legal_entities_tenant_legal_entity_active",
                table: "leave_policy_legal_entities",
                columns: new[] { "tenant_id", "legal_entity_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_approver_employee_id",
                table: "leave_request_approvers",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_leave_request_id",
                table: "leave_request_approvers",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_tenant_approver_status",
                table: "leave_request_approvers",
                columns: new[] { "tenant_id", "approver_employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_approvers_tenant_request",
                table: "leave_request_approvers",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_file_record_id",
                table: "leave_request_documents",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_leave_request_id",
                table: "leave_request_documents",
                column: "leave_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_request_documents_tenant_request",
                table: "leave_request_documents",
                columns: new[] { "tenant_id", "leave_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_employee_id",
                table: "leave_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_leave_type_id",
                table: "leave_requests",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_employee",
                table: "leave_requests",
                columns: new[] { "tenant_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests",
                columns: new[] { "tenant_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_status",
                table: "leave_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id",
                table: "leave_types",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_code",
                table: "leave_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_types_tenant_id_name",
                table: "leave_types",
                columns: new[] { "tenant_id", "name" },
                unique: true);

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
                name: "leave_approval_delegates");

            migrationBuilder.DropTable(
                name: "leave_balance_audits");

            migrationBuilder.DropTable(
                name: "leave_entitlements");

            migrationBuilder.DropTable(
                name: "leave_policy_blackout_periods");

            migrationBuilder.DropTable(
                name: "leave_policy_leave_types");

            migrationBuilder.DropTable(
                name: "leave_policy_legal_entities");

            migrationBuilder.DropTable(
                name: "leave_request_approvers");

            migrationBuilder.DropTable(
                name: "leave_request_documents");

            migrationBuilder.DropTable(
                name: "leave_policies");

            migrationBuilder.DropTable(
                name: "leave_requests");

            migrationBuilder.DropTable(
                name: "leave_types");
        }
    }
}
