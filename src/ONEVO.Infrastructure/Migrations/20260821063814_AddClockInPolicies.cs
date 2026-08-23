using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClockInPolicies : Migration
    {
        // Same admin-bypass tenant_isolation policy pattern as AddDepartments.
        // TenantTables array shape is required by TenantIsolationArchitectureTests.
        private static readonly string[] TenantTables =
        [
            "clock_in_policies",
            "clock_in_late_deduction_rules"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clock_in_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    department_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    position_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    employee_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    location_verification_required = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_radius_meters = table.Column<int>(type: "integer", nullable: true),
                    onsite_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    onsite_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    remote_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    remote_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    either_photo_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_location_check_required = table.Column<bool>(type: "boolean", nullable: false),
                    either_source_rule = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    field_biometric_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_web_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_tray_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    field_photo_requirement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correction_requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    notification_recipient_resolver = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_in_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_clock_in_policies_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clock_in_late_deduction_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clock_in_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    late_arrival_minute = table.Column<int>(type: "integer", nullable: false),
                    multiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    time_off_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clock_in_late_deduction_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_clock_in_late_deduction_rules_clock_in_policies_clock_in_po",
                        column: x => x.clock_in_policy_id,
                        principalTable: "clock_in_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_clock_in_policy_id",
                table: "clock_in_late_deduction_rules",
                column: "clock_in_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_id",
                table: "clock_in_late_deduction_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_id_policy_id",
                table: "clock_in_late_deduction_rules",
                columns: new[] { "tenant_id", "clock_in_policy_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_late_deduction_rules_tenant_policy_minute",
                table: "clock_in_late_deduction_rules",
                columns: new[] { "tenant_id", "clock_in_policy_id", "late_arrival_minute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_legal_entity_id",
                table: "clock_in_policies",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id",
                table: "clock_in_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_id_legal_entity_id",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clock_in_policies_tenant_le_active_scope",
                table: "clock_in_policies",
                columns: new[] { "tenant_id", "legal_entity_id", "is_active", "scope_type" });

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
                name: "clock_in_late_deduction_rules");

            migrationBuilder.DropTable(
                name: "clock_in_policies");
        }
    }
}
