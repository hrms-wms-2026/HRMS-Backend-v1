using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeavePolicyPerTypeVersionsAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One-way conversion of the old multi-type, legal-entity-linked policies into one
            // single-type policy per (old policy, leave type) with a version 1 and company-level
            // assignments. Old data is copied into temp tables first, because the new policy rows
            // cannot be inserted while the old NOT NULL policy columns still exist.

            // --- leave types: colour replaces category; document threshold moves to hours ---
            migrationBuilder.AddColumn<string>(
                name: "color", table: "leave_types", type: "character varying(9)", maxLength: 9,
                nullable: false, defaultValue: "#3B82F6");
            migrationBuilder.AddColumn<decimal>(
                name: "document_required_after_hours", table: "leave_types", type: "numeric(7,2)", nullable: true);
            // Every table here has FORCE ROW LEVEL SECURITY: without admin mode the migrator sees
            // no rows and the conversion silently copies nothing. Transaction-local, so it covers
            // every statement of this migration.
            migrationBuilder.Sql("SELECT set_config('app.tenant_context_mode', 'admin', true);");
            migrationBuilder.Sql(@"
                UPDATE leave_types SET color = CASE category
                    WHEN 'annual' THEN '#2563EB' WHEN 'sick' THEN '#16A34A' WHEN 'maternity' THEN '#DB2777'
                    WHEN 'paternity' THEN '#7C3AED' WHEN 'compassionate' THEN '#64748B' WHEN 'unpaid' THEN '#EA580C'
                    ELSE '#475569' END;
                UPDATE leave_types SET document_required_after_hours = document_required_after_days * 8
                    WHERE document_required_after_days IS NOT NULL AND document_required_after_days > 0;");

            // --- snapshot the old policy data ---
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE _lp_src ON COMMIT DROP AS
                SELECT gen_random_uuid() AS new_policy_id, gen_random_uuid() AS new_version_id,
                       p.id AS old_policy_id, p.tenant_id, p.name AS policy_name, p.description, p.is_active,
                       p.created_at, p.effective_from, p.accrual_method, p.accrual_start, p.accrual_after_n_months,
                       p.probation_restriction, p.minimum_tenure_months, p.min_days_per_request,
                       p.max_consecutive_days, p.minimum_notice_days, p.max_team_absence_percent, p.approval_mode,
                       plt.leave_type_id, t.name AS type_name, plt.annual_entitlement_days,
                       plt.carry_forward_max_days, plt.carry_forward_expiry_months,
                       COALESCE((
                           SELECT CASE WHEN w.h > 0 THEN w.h ELSE NULL END FROM (
                               SELECT (EXTRACT(EPOCH FROM (le.work_end_time - le.work_start_time)) / 3600.0
                                       + CASE WHEN le.work_end_time <= le.work_start_time THEN 24 ELSE 0 END
                                       - COALESCE(le.break_duration_minutes, 0) / 60.0) AS h
                               FROM leave_policy_legal_entities x
                               JOIN legal_entities le ON le.id = x.legal_entity_id
                               WHERE x.leave_policy_id = p.id
                                 AND le.work_start_time IS NOT NULL AND le.work_end_time IS NOT NULL
                               ORDER BY x.is_active DESC, x.effective_date
                               LIMIT 1) w), 8) AS hours_per_day
                FROM leave_policies p
                JOIN leave_policy_leave_types plt ON plt.leave_policy_id = p.id
                JOIN leave_types t ON t.id = plt.leave_type_id;

                CREATE TEMP TABLE _lp_le ON COMMIT DROP AS
                SELECT leave_policy_id AS old_policy_id, legal_entity_id, is_active FROM leave_policy_legal_entities;

                CREATE TEMP TABLE _lp_blackouts ON COMMIT DROP AS
                SELECT leave_policy_id AS old_policy_id, start_date, end_date, reason FROM leave_policy_blackout_periods;

                DO $guard$
                BEGIN
                    IF (SELECT count(*) FROM leave_policy_leave_types) <> (SELECT count(*) FROM _lp_src) THEN
                        RAISE EXCEPTION 'leave policy conversion would lose rows (RLS admin mode not active?)';
                    END IF;
                END
                $guard$;

                DELETE FROM leave_policy_blackout_periods;
                DELETE FROM leave_policy_leave_types;
                DELETE FROM leave_policy_legal_entities;
                DELETE FROM leave_policies;");

            // --- drop the old shape ---
            migrationBuilder.DropForeignKey(
                name: "fk_leave_policy_blackout_periods_leave_policies_leave_policy_id",
                table: "leave_policy_blackout_periods");
            migrationBuilder.DropTable(name: "leave_policy_leave_types");
            migrationBuilder.DropTable(name: "leave_policy_legal_entities");
            foreach (var column in new[]
                     {
                         "carry_forward_allowed", "carry_forward_expiry_months", "category", "default_days_per_year",
                         "document_required_after_days", "max_carry_forward_days", "max_consecutive_days",
                         "minimum_notice_days", "pro_rata_for_new_joiners", "requires_approval"
                     })
                migrationBuilder.DropColumn(name: column, table: "leave_types");
            foreach (var column in new[]
                     {
                         "accrual_after_n_months", "accrual_method", "accrual_start", "approval_mode", "country",
                         "effective_from", "first_year_reduced_percent", "job_level", "max_consecutive_days",
                         "max_team_absence_percent", "min_days_per_request", "minimum_notice_days",
                         "minimum_tenure_months", "probation_restriction", "proration_method", "version"
                     })
                migrationBuilder.DropColumn(name: column, table: "leave_policies");

            migrationBuilder.RenameColumn(
                name: "leave_policy_id", table: "leave_policy_blackout_periods", newName: "leave_policy_version_id");
            migrationBuilder.RenameIndex(
                name: "ix_leave_policy_blackout_periods_tenant_policy", table: "leave_policy_blackout_periods",
                newName: "ix_leave_policy_blackout_periods_tenant_version");
            migrationBuilder.RenameIndex(
                name: "ix_leave_policy_blackout_periods_leave_policy_id", table: "leave_policy_blackout_periods",
                newName: "ix_leave_policy_blackout_periods_leave_policy_version_id");

            migrationBuilder.AlterColumn<string>(
                name: "name", table: "leave_policies", type: "character varying(150)", maxLength: 150,
                nullable: false, oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
            migrationBuilder.AlterColumn<string>(
                name: "description", table: "leave_policies", type: "character varying(1000)", maxLength: 1000,
                nullable: true, oldClrType: typeof(string), oldType: "text", oldNullable: true);
            migrationBuilder.AddColumn<Guid>(
                name: "leave_type_id", table: "leave_policies", type: "uuid", nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "leave_policy_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_assignments_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leave_policy_assignments_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_policy_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_hours = table.Column<decimal>(type: "numeric(7,2)", nullable: false),
                    amount_period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    carry_forward_cap_hours = table.Column<decimal>(type: "numeric(7,2)", nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "integer", nullable: true),
                    eligible_from = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    eligible_after_months = table.Column<int>(type: "integer", nullable: true),
                    minimum_notice_days = table.Column<int>(type: "integer", nullable: false),
                    min_request_hours = table.Column<decimal>(type: "numeric(7,2)", nullable: false),
                    max_request_hours = table.Column<decimal>(type: "numeric(7,2)", nullable: true),
                    max_team_absence_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    approval_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_policy_versions_leave_policies_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalTable: "leave_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_leave_type_id",
                table: "leave_policies",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policies_tenant_leave_type",
                table: "leave_policies",
                columns: new[] { "tenant_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_leave_policy_id",
                table: "leave_policy_assignments",
                column: "leave_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_leave_type_id",
                table: "leave_policy_assignments",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_open_target_leave_type",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "target_kind", "target_id", "leave_type_id" },
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_tenant_policy",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "leave_policy_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_assignments_tenant_target",
                table: "leave_policy_assignments",
                columns: new[] { "tenant_id", "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_policy_effective_from",
                table: "leave_policy_versions",
                columns: new[] { "leave_policy_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_policy_version_number",
                table: "leave_policy_versions",
                columns: new[] { "leave_policy_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_versions_tenant_id",
                table: "leave_policy_versions",
                column: "tenant_id");


            // --- convert: one policy + version 1 per old (policy, leave type); blackouts copied per version ---
            migrationBuilder.Sql(@"
                INSERT INTO leave_policies (id, tenant_id, leave_type_id, name, description, is_active, created_at)
                SELECT new_policy_id, tenant_id, leave_type_id, LEFT(policy_name || ' - ' || type_name, 150),
                       description, is_active, created_at
                FROM _lp_src;

                INSERT INTO leave_policy_versions (
                    id, tenant_id, leave_policy_id, version_number, effective_from, amount_hours, amount_period,
                    carry_forward_cap_hours, carry_forward_expiry_months, eligible_from, eligible_after_months,
                    minimum_notice_days, min_request_hours, max_request_hours, max_team_absence_percent,
                    approval_mode, created_at)
                SELECT new_version_id, tenant_id, new_policy_id, 1, effective_from,
                       ROUND(CASE WHEN accrual_method = 'monthly'
                                  THEN annual_entitlement_days * hours_per_day / 12.0
                                  ELSE annual_entitlement_days * hours_per_day END, 2),
                       CASE WHEN accrual_method = 'monthly' THEN 'month' ELSE 'year' END,
                       ROUND(carry_forward_max_days * hours_per_day, 2),
                       carry_forward_expiry_months,
                       CASE WHEN probation_restriction OR accrual_start = 'after_probation' THEN 'after_probation'
                            WHEN GREATEST(COALESCE(accrual_after_n_months, 0), minimum_tenure_months) > 0 THEN 'after_months'
                            ELSE 'immediately' END,
                       CASE WHEN NOT (probation_restriction OR accrual_start = 'after_probation')
                                 AND GREATEST(COALESCE(accrual_after_n_months, 0), minimum_tenure_months) > 0
                            THEN GREATEST(COALESCE(accrual_after_n_months, 0), minimum_tenure_months) END,
                       minimum_notice_days,
                       GREATEST(ROUND(min_days_per_request * hours_per_day, 2), 0.5),
                       ROUND(max_consecutive_days * hours_per_day, 2),
                       max_team_absence_percent, approval_mode, created_at
                FROM _lp_src;

                INSERT INTO leave_policy_blackout_periods (id, tenant_id, leave_policy_version_id, start_date, end_date, reason)
                SELECT gen_random_uuid(), s.tenant_id, s.new_version_id, b.start_date, b.end_date, b.reason
                FROM _lp_src s JOIN _lp_blackouts b ON b.old_policy_id = s.old_policy_id;

                INSERT INTO leave_policy_assignments (
                    id, tenant_id, leave_policy_id, leave_type_id, target_kind, target_id, effective_from, created_at)
                SELECT gen_random_uuid(), s.tenant_id, s.new_policy_id, s.leave_type_id, 'legal_entity',
                       le.legal_entity_id, s.effective_from, s.created_at
                FROM _lp_src s JOIN _lp_le le ON le.old_policy_id = s.old_policy_id AND le.is_active
                WHERE s.is_active
                ON CONFLICT DO NOTHING;");

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policies_leave_types_leave_type_id",
                table: "leave_policies",
                column: "leave_type_id",
                principalTable: "leave_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_leave_policy_blackout_periods_leave_policy_versions_leave_p",
                table: "leave_policy_blackout_periods",
                column: "leave_policy_version_id",
                principalTable: "leave_policy_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One-way: the old multi-type policies cannot be rebuilt from single-type versions.
            throw new NotSupportedException(
                "LeavePolicyPerTypeVersionsAssignments is one-way; restore from a backup to go back.");
        }
    }
}
