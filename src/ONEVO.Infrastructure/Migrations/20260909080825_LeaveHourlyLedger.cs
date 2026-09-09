using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LeaveHourlyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "end_at",
                table: "leave_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "paid_hours",
                table: "leave_requests",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "start_at",
                table: "leave_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "total_hours",
                table: "leave_requests",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unpaid_hours",
                table: "leave_requests",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "hours_unit",
                table: "leave_request_day_allocations",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_hours_unit",
                table: "leave_request_day_allocations",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unpaid_hours_unit",
                table: "leave_request_day_allocations",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "carried_forward_hours",
                table: "leave_entitlements",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_hours",
                table: "leave_entitlements",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_hours",
                table: "leave_entitlements",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "used_hours",
                table: "leave_entitlements",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "balance_after",
                table: "leave_balance_audits",
                type: "numeric(8,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,1)");

            migrationBuilder.AddColumn<decimal>(
                name: "hours_changed",
                table: "leave_balance_audits",
                type: "numeric(8,2)",
                nullable: false,
                defaultValue: 0m);

            // Convert existing day amounts using each employee's legal-entity work window.
            // Unset windows use 8.00 hours and 09:00–17:00 only for this backfill.
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                CREATE TEMP TABLE leave_hourly_ledger_windows ON COMMIT DROP AS
                SELECT
                    e.id AS employee_id,
                    COALESCE(
                        (SELECT n.name FROM pg_timezone_names AS n WHERE n.name = le.timezone),
                        'UTC') AS tz,
                    COALESCE(le.work_start_time, TIME '09:00') AS work_start,
                    COALESCE(le.work_end_time, TIME '17:00') AS work_end,
                    CASE
                        WHEN le.work_start_time IS NULL OR le.work_end_time IS NULL THEN 8.00
                        WHEN computed.hours > 0 THEN computed.hours
                        ELSE 8.00
                    END AS work_day_hours
                FROM employees AS e
                LEFT JOIN legal_entities AS le ON le.id = e.legal_entity_id
                CROSS JOIN LATERAL (
                    SELECT ROUND(
                        (
                            (
                                CASE
                                    WHEN le.work_start_time IS NULL OR le.work_end_time IS NULL THEN 0
                                    WHEN le.work_end_time <= le.work_start_time
                                        THEN EXTRACT(EPOCH FROM (le.work_end_time - le.work_start_time)) + 86400
                                    ELSE EXTRACT(EPOCH FROM (le.work_end_time - le.work_start_time))
                                END
                                - (COALESCE(le.break_duration_minutes, 0) * 60.0)
                            ) / 3600.0
                        )::numeric,
                        2) AS hours
                ) AS computed;

                CREATE INDEX ON leave_hourly_ledger_windows (employee_id);

                UPDATE leave_entitlements AS ent
                SET
                    total_hours = ROUND(ent.total_days * w.work_day_hours, 2),
                    used_hours = ROUND(ent.used_days * w.work_day_hours, 2),
                    pending_hours = ROUND(ent.pending_days * w.work_day_hours, 2),
                    carried_forward_hours = ROUND(ent.carried_forward_days * w.work_day_hours, 2)
                FROM leave_hourly_ledger_windows AS w
                WHERE w.employee_id = ent.employee_id;

                UPDATE leave_balance_audits AS a
                SET
                    hours_changed = ROUND(a.days_changed * w.work_day_hours, 2),
                    balance_after = ROUND(a.balance_after * w.work_day_hours, 2)
                FROM leave_hourly_ledger_windows AS w
                WHERE w.employee_id = a.employee_id;

                UPDATE leave_requests AS lr
                SET
                    total_hours = ROUND(lr.total_days * w.work_day_hours, 2),
                    paid_hours = ROUND(lr.paid_days * w.work_day_hours, 2),
                    unpaid_hours = ROUND(lr.unpaid_days * w.work_day_hours, 2),
                    start_at = CASE LOWER(COALESCE(lr.half_day_period, ''))
                        WHEN 'pm' THEN
                            (
                                CASE
                                    WHEN w.work_end <= w.work_start
                                        THEN ((lr.start_date + 1) + w.work_end)
                                    ELSE (lr.start_date + w.work_end)
                                END AT TIME ZONE w.tz
                            ) - ((w.work_day_hours / 2.0)::double precision * INTERVAL '1 hour')
                        ELSE
                            (lr.start_date + w.work_start) AT TIME ZONE w.tz
                    END,
                    end_at = CASE LOWER(COALESCE(lr.half_day_period, ''))
                        WHEN 'am' THEN
                            ((lr.start_date + w.work_start) AT TIME ZONE w.tz)
                            + ((w.work_day_hours / 2.0)::double precision * INTERVAL '1 hour')
                        WHEN 'pm' THEN
                            CASE
                                WHEN w.work_end <= w.work_start
                                    THEN ((lr.start_date + 1) + w.work_end)
                                ELSE (lr.start_date + w.work_end)
                            END AT TIME ZONE w.tz
                        ELSE
                            CASE
                                WHEN w.work_end <= w.work_start
                                    THEN ((lr.end_date + 1) + w.work_end)
                                ELSE (lr.end_date + w.work_end)
                            END AT TIME ZONE w.tz
                    END
                FROM leave_hourly_ledger_windows AS w
                WHERE w.employee_id = lr.employee_id;

                UPDATE leave_request_day_allocations AS a
                SET
                    hours_unit = ROUND(a.day_unit * w.work_day_hours, 2),
                    paid_hours_unit = ROUND(a.paid_unit * w.work_day_hours, 2),
                    unpaid_hours_unit = ROUND(a.unpaid_unit * w.work_day_hours, 2)
                FROM leave_requests AS lr
                JOIN leave_hourly_ledger_windows AS w ON w.employee_id = lr.employee_id
                WHERE lr.id = a.leave_request_id;

                ALTER TABLE leave_requests
                    ALTER COLUMN start_at DROP DEFAULT,
                    ALTER COLUMN end_at DROP DEFAULT,
                    ALTER COLUMN total_hours DROP DEFAULT,
                    ALTER COLUMN paid_hours DROP DEFAULT,
                    ALTER COLUMN unpaid_hours DROP DEFAULT;

                ALTER TABLE leave_request_day_allocations
                    ALTER COLUMN hours_unit DROP DEFAULT,
                    ALTER COLUMN paid_hours_unit DROP DEFAULT,
                    ALTER COLUMN unpaid_hours_unit DROP DEFAULT;

                ALTER TABLE leave_entitlements
                    ALTER COLUMN total_hours DROP DEFAULT,
                    ALTER COLUMN used_hours DROP DEFAULT,
                    ALTER COLUMN pending_hours DROP DEFAULT,
                    ALTER COLUMN carried_forward_hours DROP DEFAULT;

                ALTER TABLE leave_balance_audits
                    ALTER COLUMN hours_changed DROP DEFAULT;

                DO $$
                BEGIN
                  IF EXISTS (
                    SELECT 1 FROM leave_requests
                    WHERE start_at = TIMESTAMPTZ '0001-01-01 00:00:00+00'
                  ) THEN
                    RAISE EXCEPTION 'LeaveHourlyLedger backfill left default start_at values';
                  END IF;

                  IF EXISTS (
                    SELECT 1 FROM leave_entitlements
                    WHERE total_days <> 0 AND total_hours = 0
                  ) THEN
                    RAISE EXCEPTION 'LeaveHourlyLedger backfill left zero total_hours for non-zero total_days';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "half_day_period",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "paid_days",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "total_days",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "unpaid_days",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "day_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "paid_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "unpaid_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "carried_forward_days",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "pending_days",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "total_days",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "used_days",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "days_changed",
                table: "leave_balance_audits");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests",
                columns: new[] { "tenant_id", "start_at", "end_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests");

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "leave_requests",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "half_day_period",
                table: "leave_requests",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_days",
                table: "leave_requests",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "leave_requests",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "total_days",
                table: "leave_requests",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unpaid_days",
                table: "leave_requests",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "day_unit",
                table: "leave_request_day_allocations",
                type: "numeric(3,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_unit",
                table: "leave_request_day_allocations",
                type: "numeric(3,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unpaid_unit",
                table: "leave_request_day_allocations",
                type: "numeric(3,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "carried_forward_days",
                table: "leave_entitlements",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_days",
                table: "leave_entitlements",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_days",
                table: "leave_entitlements",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "used_days",
                table: "leave_entitlements",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "days_changed",
                table: "leave_balance_audits",
                type: "numeric(5,1)",
                nullable: false,
                defaultValue: 0m);

            // Reverse hours with a fixed 8.00-hour day; AM/PM is not restored.
            migrationBuilder.Sql("""
                SET LOCAL app.tenant_context_mode = 'admin';

                CREATE TEMP TABLE leave_hourly_ledger_windows ON COMMIT DROP AS
                SELECT
                    e.id AS employee_id,
                    COALESCE(
                        (SELECT n.name FROM pg_timezone_names AS n WHERE n.name = le.timezone),
                        'UTC') AS tz
                FROM employees AS e
                LEFT JOIN legal_entities AS le ON le.id = e.legal_entity_id;

                CREATE INDEX ON leave_hourly_ledger_windows (employee_id);

                UPDATE leave_entitlements
                SET
                    total_days = ROUND(total_hours / 8.00, 1),
                    used_days = ROUND(used_hours / 8.00, 1),
                    pending_days = ROUND(pending_hours / 8.00, 1),
                    carried_forward_days = ROUND(carried_forward_hours / 8.00, 1);

                UPDATE leave_balance_audits
                SET
                    days_changed = ROUND(hours_changed / 8.00, 1),
                    balance_after = ROUND(balance_after / 8.00, 1);

                UPDATE leave_requests AS lr
                SET
                    total_days = ROUND(lr.total_hours / 8.00, 1),
                    paid_days = ROUND(lr.paid_hours / 8.00, 1),
                    unpaid_days = ROUND(lr.unpaid_hours / 8.00, 1),
                    start_date = (lr.start_at AT TIME ZONE COALESCE(w.tz, 'UTC'))::date,
                    end_date = (lr.end_at AT TIME ZONE COALESCE(w.tz, 'UTC'))::date,
                    half_day_period = NULL
                FROM leave_hourly_ledger_windows AS w
                WHERE w.employee_id = lr.employee_id;

                UPDATE leave_request_day_allocations
                SET
                    day_unit = ROUND(hours_unit / 8.00, 1),
                    paid_unit = ROUND(paid_hours_unit / 8.00, 1),
                    unpaid_unit = ROUND(unpaid_hours_unit / 8.00, 1);

                ALTER TABLE leave_requests
                    ALTER COLUMN start_date DROP DEFAULT,
                    ALTER COLUMN end_date DROP DEFAULT,
                    ALTER COLUMN total_days DROP DEFAULT,
                    ALTER COLUMN paid_days DROP DEFAULT,
                    ALTER COLUMN unpaid_days DROP DEFAULT;

                ALTER TABLE leave_request_day_allocations
                    ALTER COLUMN day_unit DROP DEFAULT,
                    ALTER COLUMN paid_unit DROP DEFAULT,
                    ALTER COLUMN unpaid_unit DROP DEFAULT;

                ALTER TABLE leave_entitlements
                    ALTER COLUMN total_days DROP DEFAULT,
                    ALTER COLUMN used_days DROP DEFAULT,
                    ALTER COLUMN pending_days DROP DEFAULT,
                    ALTER COLUMN carried_forward_days DROP DEFAULT;

                ALTER TABLE leave_balance_audits
                    ALTER COLUMN days_changed DROP DEFAULT;
                """);

            migrationBuilder.DropColumn(
                name: "end_at",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "paid_hours",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "start_at",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "total_hours",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "unpaid_hours",
                table: "leave_requests");

            migrationBuilder.DropColumn(
                name: "hours_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "paid_hours_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "unpaid_hours_unit",
                table: "leave_request_day_allocations");

            migrationBuilder.DropColumn(
                name: "carried_forward_hours",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "pending_hours",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "total_hours",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "used_hours",
                table: "leave_entitlements");

            migrationBuilder.DropColumn(
                name: "hours_changed",
                table: "leave_balance_audits");

            migrationBuilder.AlterColumn<decimal>(
                name: "balance_after",
                table: "leave_balance_audits",
                type: "numeric(5,1)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_tenant_start_end",
                table: "leave_requests",
                columns: new[] { "tenant_id", "start_date", "end_date" });
        }
    }
}
