using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810160000_AddEmployeeOnboardingPhase1PersistenceContracts")]
public partial class AddEmployeeOnboardingPhase1PersistenceContracts : Migration
{
    private static readonly string[] TenantTables = ["access_grant_requests", "checklist_templates", "employee_checklist_tasks"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "checklist_templates", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
            name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
            template_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            department_id = table.Column<Guid>(type: "uuid", nullable: true), tasks_json = table.Column<string>(type: "jsonb", nullable: false),
            is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
        }, constraints: table => { table.PrimaryKey("pk_checklist_templates", x => x.id); table.ForeignKey("fk_checklist_templates_departments_department_id", x => x.department_id, "departments", "id", onDelete: ReferentialAction.Restrict); });

        migrationBuilder.CreateTable(name: "access_grant_requests", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
            employee_id = table.Column<Guid>(type: "uuid", nullable: true), user_id = table.Column<Guid>(type: "uuid", nullable: true),
            onboarding_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
            action_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false), target_position_id = table.Column<Guid>(type: "uuid", nullable: false),
            target_department_id = table.Column<Guid>(type: "uuid", nullable: false), position_access_template_id = table.Column<Guid>(type: "uuid", nullable: false),
            requested_role_id = table.Column<Guid>(type: "uuid", nullable: false), approval_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false), decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
            requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
        }, constraints: table => { table.PrimaryKey("pk_access_grant_requests", x => x.id); table.ForeignKey("fk_access_grant_requests_employees_employee_id", x => x.employee_id, "employees", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_users_requested_by_user_id", x => x.requested_by_user_id, "users", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_users_decided_by_user_id", x => x.decided_by_user_id, "users", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_positions_target_position_id", x => x.target_position_id, "positions", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_departments_target_department_id", x => x.target_department_id, "departments", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_position_access_templates_position_access_template_id", x => x.position_access_template_id, "position_access_templates", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_roles_requested_role_id", x => x.requested_role_id, "roles", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_access_grant_requests_onboarding_drafts_onboarding_draft_id", x => x.onboarding_draft_id, "onboarding_drafts", "id", onDelete: ReferentialAction.Restrict); });

        migrationBuilder.CreateTable(name: "employee_checklist_tasks", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false), tenant_id = table.Column<Guid>(type: "uuid", nullable: false), employee_id = table.Column<Guid>(type: "uuid", nullable: false),
            template_id = table.Column<Guid>(type: "uuid", nullable: true), lifecycle_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            task_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false), owner_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
            sequence = table.Column<int>(type: "integer", nullable: true), assigned_to_id = table.Column<Guid>(type: "uuid", nullable: false), due_date = table.Column<DateOnly>(type: "date", nullable: false),
            status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: table => { table.PrimaryKey("pk_employee_checklist_tasks", x => x.id); table.ForeignKey("fk_employee_checklist_tasks_employees_employee_id", x => x.employee_id, "employees", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_employee_checklist_tasks_checklist_templates_template_id", x => x.template_id, "checklist_templates", "id", onDelete: ReferentialAction.Restrict); table.ForeignKey("fk_employee_checklist_tasks_users_assigned_to_id", x => x.assigned_to_id, "users", "id", onDelete: ReferentialAction.Restrict); });

        migrationBuilder.CreateIndex("ix_checklist_templates_department_id", "checklist_templates", "department_id");
        migrationBuilder.CreateIndex("ix_checklist_templates_tenant_id_department_id", "checklist_templates", new[] { "tenant_id", "department_id" });
        migrationBuilder.CreateIndex("ix_checklist_templates_tenant_id_template_type_is_active", "checklist_templates", new[] { "tenant_id", "template_type", "is_active" });
        migrationBuilder.CreateIndex("ix_access_grant_requests_decided_by_user_id", "access_grant_requests", "decided_by_user_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_employee_id", "access_grant_requests", "employee_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_position_access_template_id", "access_grant_requests", "position_access_template_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_requested_by_user_id", "access_grant_requests", "requested_by_user_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_requested_role_id", "access_grant_requests", "requested_role_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_target_department_id", "access_grant_requests", "target_department_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_target_position_id", "access_grant_requests", "target_position_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_user_id", "access_grant_requests", "user_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_onboarding_draft_id", "access_grant_requests", "onboarding_draft_id");
        migrationBuilder.CreateIndex("ix_access_grant_requests_one_pending", "access_grant_requests", new[] { "tenant_id", "onboarding_draft_id", "target_position_id", "position_access_template_id" }, unique: true, filter: "approval_status = 'Pending'");
        migrationBuilder.CreateIndex("ix_employee_checklist_tasks_assigned_to_id", "employee_checklist_tasks", "assigned_to_id");
        migrationBuilder.CreateIndex("ix_employee_checklist_tasks_employee_id", "employee_checklist_tasks", "employee_id");
        migrationBuilder.CreateIndex("ix_employee_checklist_tasks_template_id", "employee_checklist_tasks", "template_id");
        migrationBuilder.CreateIndex("ix_employee_checklist_tasks_tenant_id_employee_id_lifecycle_type_sequence", "employee_checklist_tasks", new[] { "tenant_id", "employee_id", "lifecycle_type", "sequence" });

        foreach (var table in TenantTables) migrationBuilder.Sql($@"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY; ALTER TABLE {table} FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ON {table} USING (current_setting('app.tenant_context_mode', true) = 'admin' OR (current_setting('app.tenant_context_mode', true) = 'tenant' AND tenant_id::text = current_setting('app.current_tenant_id', true))) WITH CHECK (current_setting('app.tenant_context_mode', true) = 'admin' OR (current_setting('app.tenant_context_mode', true) = 'tenant' AND tenant_id::text = current_setting('app.current_tenant_id', true))); ");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { foreach (var table in TenantTables) migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {table}; ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;"); migrationBuilder.DropTable("access_grant_requests"); migrationBuilder.DropTable("employee_checklist_tasks"); migrationBuilder.DropTable("checklist_templates"); }
}
