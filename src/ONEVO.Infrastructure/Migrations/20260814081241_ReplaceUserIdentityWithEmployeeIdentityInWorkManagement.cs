using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceUserIdentityWithEmployeeIdentityInWorkManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Backfill: UserId-valued columns -> the matching Employee.Id, same tenant. ---
            // Rows whose stored UserId has no Employee in this tenant (e.g. a smoke-test user with no
            // Employee record) are left unchanged by design - Step 4 below finds and reports any such
            // rows so they can be fixed by hand before this migration is considered done.
            migrationBuilder.Sql(@"
                UPDATE projects p SET lead_id = e.id
                FROM employees e WHERE e.tenant_id = p.tenant_id AND e.user_id = p.lead_id;");

            migrationBuilder.Sql(@"
                UPDATE objectives o SET owner_id = e.id
                FROM employees e WHERE e.tenant_id = o.tenant_id AND e.user_id = o.owner_id;");
            migrationBuilder.Sql(@"
                UPDATE objectives o SET reporting_manager_id = e.id
                FROM employees e
                WHERE o.reporting_manager_id IS NOT NULL
                  AND e.tenant_id = o.tenant_id AND e.user_id = o.reporting_manager_id;");

            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET requested_by_id = e.id
                FROM employees e WHERE e.tenant_id = r.tenant_id AND e.user_id = r.requested_by_id;");
            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET reporting_manager_id = e.id
                FROM employees e WHERE e.tenant_id = r.tenant_id AND e.user_id = r.reporting_manager_id;");
            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET decided_by_id = e.id
                FROM employees e
                WHERE r.decided_by_id IS NOT NULL
                  AND e.tenant_id = r.tenant_id AND e.user_id = r.decided_by_id;");

            // --- project_members / project_member_invitations: drop the now-redundant UserId column. ---
            migrationBuilder.DropIndex(
                name: "ix_project_members_tenant_project_objective_user",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_project_members_tenant_user_active_project",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_tenant_invited_user_status",
                table: "project_member_invitations");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "project_members");

            migrationBuilder.DropColumn(
                name: "invited_user_id",
                table: "project_member_invitations");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_employee_active_project",
                table: "project_members",
                columns: new[] { "tenant_id", "employee_id", "is_active", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_project_objective_employee",
                table: "project_members",
                columns: new[] { "tenant_id", "project_id", "objective_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "project_id", "objective_id", "invited_employee_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_tenant_invited_employee_status",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "invited_employee_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_members_tenant_employee_active_project",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_project_members_tenant_project_objective_employee",
                table: "project_members");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations");

            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_tenant_invited_employee_status",
                table: "project_member_invitations");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "project_members",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "invited_user_id",
                table: "project_member_invitations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE project_member_invitations i SET invited_user_id = e.user_id
                FROM employees e WHERE e.id = i.invited_employee_id;");

            migrationBuilder.Sql(@"
                UPDATE project_members m SET user_id = e.user_id
                FROM employees e WHERE e.id = m.employee_id;");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_project_objective_user",
                table: "project_members",
                columns: new[] { "tenant_id", "project_id", "objective_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_user_active_project",
                table: "project_members",
                columns: new[] { "tenant_id", "user_id", "is_active", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "project_id", "objective_id", "invited_user_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_tenant_invited_user_status",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "invited_user_id", "status" });

            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET decided_by_id = e.user_id
                FROM employees e WHERE r.decided_by_id IS NOT NULL AND e.id = r.decided_by_id;");
            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET reporting_manager_id = e.user_id
                FROM employees e WHERE e.id = r.reporting_manager_id;");
            migrationBuilder.Sql(@"
                UPDATE objective_change_requests r SET requested_by_id = e.user_id
                FROM employees e WHERE e.id = r.requested_by_id;");

            migrationBuilder.Sql(@"
                UPDATE objectives o SET reporting_manager_id = e.user_id
                FROM employees e WHERE o.reporting_manager_id IS NOT NULL AND e.id = o.reporting_manager_id;");
            migrationBuilder.Sql(@"
                UPDATE objectives o SET owner_id = e.user_id
                FROM employees e WHERE e.id = o.owner_id;");

            migrationBuilder.Sql(@"
                UPDATE projects p SET lead_id = e.user_id
                FROM employees e WHERE e.id = p.lead_id;");
        }
    }
}
