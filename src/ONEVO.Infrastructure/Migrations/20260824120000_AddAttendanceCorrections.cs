using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

public partial class AddAttendanceCorrections : Migration
{
    private static readonly string[] TenantTables = ["attendance_corrections"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attendance_corrections",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                presence_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                attendance_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                work_date = table.Column<DateOnly>(type: "date", nullable: false),
                correction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                original_clock_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                original_clock_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                requested_clock_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                requested_clock_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                original_break_json = table.Column<string>(type: "jsonb", nullable: true),
                requested_break_json = table.Column<string>(type: "jsonb", nullable: true),
                reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                notes = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                review_comment = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_attendance_corrections", x => x.id);
                table.ForeignKey("fk_attendance_corrections_attendance_records_attendance_record_id", x => x.attendance_record_id, "attendance_records", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_attendance_corrections_employees_employee_id", x => x.employee_id, "employees", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_attendance_corrections_legal_entities_legal_entity_id", x => x.legal_entity_id, "legal_entities", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_attendance_corrections_presence_sessions_presence_session_id", x => x.presence_session_id, "presence_sessions", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_attendance_corrections_users_requested_by_id", x => x.requested_by_id, "users", "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_attendance_corrections_users_reviewed_by_id", x => x.reviewed_by_id, "users", "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("ix_attendance_corrections_tenant_legal_entity_employee_created_at", "attendance_corrections", new[] { "tenant_id", "legal_entity_id", "employee_id", "created_at" });
        migrationBuilder.CreateIndex("ix_attendance_corrections_tenant_legal_entity_status_created_at", "attendance_corrections", new[] { "tenant_id", "legal_entity_id", "status", "created_at" });
        migrationBuilder.CreateIndex("ix_attendance_corrections_attendance_record_id", "attendance_corrections", "attendance_record_id");
        migrationBuilder.CreateIndex("ix_attendance_corrections_tenant_employee_work_date_type", "attendance_corrections", new[] { "tenant_id", "employee_id", "work_date", "correction_type" });
        migrationBuilder.CreateIndex("ux_attendance_corrections_pending_record_type", "attendance_corrections", new[] { "tenant_id", "employee_id", "work_date", "correction_type" }, unique: true, filter: "status = 'pending'");

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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in TenantTables)
        {
            migrationBuilder.Sql($@"
                DROP POLICY IF EXISTS tenant_isolation ON {table};
                ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
            ");
        }

        migrationBuilder.DropTable(name: "attendance_corrections");
    }
}
