using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveLedgerPolicyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "exceeds_balance_by_hours",
                table: "leave_requests",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_policy_version_id",
                table: "leave_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_policy_id",
                table: "leave_entitlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_policy_version_id",
                table: "leave_entitlements",
                type: "uuid",
                nullable: true);

            // xmin is a PostgreSQL system column that already exists on every row. The EF model
            // maps it as a shadow concurrency token; attempting to add/drop it is invalid DDL.

            migrationBuilder.AddColumn<int>(
                name: "covers_from_month",
                table: "leave_balance_audits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "covers_to_month",
                table: "leave_balance_audits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_policy_id",
                table: "leave_balance_audits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_policy_version_id",
                table: "leave_balance_audits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "year",
                table: "leave_balance_audits",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_balance_audits_tenant_employee_type_year",
                table: "leave_balance_audits",
                columns: new[] { "tenant_id", "employee_id", "leave_type_id", "year" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_leave_balance_audits_covers_months",
                table: "leave_balance_audits",
                sql: "(covers_from_month IS NULL AND covers_to_month IS NULL) OR (covers_from_month BETWEEN 1 AND 12 AND covers_to_month BETWEEN covers_from_month AND 12)");

            migrationBuilder.Sql("SELECT set_config('app.tenant_context_mode', 'admin', true);");
            migrationBuilder.Sql(@"
                WITH resolved AS (
                    SELECT e.id AS entitlement_id,
                           a.leave_policy_id,
                           v.id AS leave_policy_version_id
                    FROM leave_entitlements e
                    JOIN employees employee ON employee.id = e.employee_id AND employee.tenant_id = e.tenant_id
                    LEFT JOIN LATERAL (
                        SELECT assignment.leave_policy_id
                        FROM leave_policy_assignments assignment
                        JOIN leave_policies policy ON policy.id = assignment.leave_policy_id
                        WHERE assignment.tenant_id = e.tenant_id
                          AND assignment.target_kind = 'legal_entity'
                          AND assignment.target_id = employee.legal_entity_id
                          AND assignment.leave_type_id = e.leave_type_id
                          AND assignment.effective_from <= make_date(e.year, 12, 31)
                          AND (assignment.effective_to IS NULL OR assignment.effective_to >= make_date(e.year, 1, 1))
                          AND policy.is_active
                        ORDER BY assignment.effective_from DESC
                        LIMIT 1
                    ) a ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT version.id
                        FROM leave_policy_versions version
                        WHERE version.leave_policy_id = a.leave_policy_id
                          AND version.effective_from <= make_date(e.year, 12, 31)
                        ORDER BY version.effective_from DESC
                        LIMIT 1
                    ) v ON TRUE
                    WHERE e.source = 'auto'
                )
                UPDATE leave_balance_audits audit
                   SET year = entitlement.year,
                       covers_from_month = 1,
                       covers_to_month = 12,
                       leave_policy_id = resolved.leave_policy_id,
                       leave_policy_version_id = resolved.leave_policy_version_id
                  FROM leave_entitlements entitlement
                  JOIN resolved ON resolved.entitlement_id = entitlement.id
                 WHERE audit.tenant_id = entitlement.tenant_id
                   AND audit.employee_id = entitlement.employee_id
                   AND audit.leave_type_id = entitlement.leave_type_id
                   AND audit.change_type = 'accrual'
                   AND audit.year IS NULL;

                WITH resolved AS (
                    SELECT e.id AS entitlement_id,
                           a.leave_policy_id,
                           v.id AS leave_policy_version_id
                    FROM leave_entitlements e
                    JOIN employees employee ON employee.id = e.employee_id AND employee.tenant_id = e.tenant_id
                    LEFT JOIN LATERAL (
                        SELECT assignment.leave_policy_id
                        FROM leave_policy_assignments assignment
                        JOIN leave_policies policy ON policy.id = assignment.leave_policy_id
                        WHERE assignment.tenant_id = e.tenant_id
                          AND assignment.target_kind = 'legal_entity'
                          AND assignment.target_id = employee.legal_entity_id
                          AND assignment.leave_type_id = e.leave_type_id
                          AND assignment.effective_from <= make_date(e.year, 12, 31)
                          AND (assignment.effective_to IS NULL OR assignment.effective_to >= make_date(e.year, 1, 1))
                          AND policy.is_active
                        ORDER BY assignment.effective_from DESC
                        LIMIT 1
                    ) a ON TRUE
                    LEFT JOIN LATERAL (
                        SELECT version.id
                        FROM leave_policy_versions version
                        WHERE version.leave_policy_id = a.leave_policy_id
                          AND version.effective_from <= make_date(e.year, 12, 31)
                        ORDER BY version.effective_from DESC
                        LIMIT 1
                    ) v ON TRUE
                    WHERE e.source = 'auto'
                )
                INSERT INTO leave_balance_audits (
                    id, tenant_id, employee_id, leave_type_id, change_type, hours_changed,
                    balance_after, reason, leave_policy_id, leave_policy_version_id,
                    year, covers_from_month, covers_to_month, created_at)
                SELECT gen_random_uuid(), entitlement.tenant_id, entitlement.employee_id,
                       entitlement.leave_type_id, 'accrual', entitlement.total_hours,
                       entitlement.total_hours + entitlement.carried_forward_hours
                           - entitlement.used_hours - entitlement.pending_hours,
                       'Opening balance (policy cleanup)', resolved.leave_policy_id,
                       resolved.leave_policy_version_id, entitlement.year, 1, 12, NOW()
                  FROM leave_entitlements entitlement
                  JOIN resolved ON resolved.entitlement_id = entitlement.id
                 WHERE entitlement.source = 'auto'
                   AND entitlement.total_hours <> 0
                   AND NOT EXISTS (
                       SELECT 1 FROM leave_balance_audits audit
                        WHERE audit.tenant_id = entitlement.tenant_id
                          AND audit.employee_id = entitlement.employee_id
                          AND audit.leave_type_id = entitlement.leave_type_id
                          AND audit.change_type = 'accrual'
                          AND audit.year = entitlement.year);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_balance_audits_tenant_employee_type_year",
                table: "leave_balance_audits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leave_balance_audits_covers_months",
                table: "leave_balance_audits");

            migrationBuilder.DropColumn(
                name: "exceeds_balance_by_hours",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "leave_policy_version_id",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "leave_policy_id",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "leave_policy_version_id",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "covers_from_month",
                table: "leave_balance_audits");

            migrationBuilder.DropColumn(
                name: "covers_to_month",
                table: "leave_balance_audits");

            migrationBuilder.DropColumn(
                name: "leave_policy_id",
                table: "leave_balance_audits");

            migrationBuilder.DropColumn(
                name: "leave_policy_version_id",
                table: "leave_balance_audits");

            migrationBuilder.DropColumn(
                name: "year",
                table: "leave_balance_audits");
        }
    }
}
