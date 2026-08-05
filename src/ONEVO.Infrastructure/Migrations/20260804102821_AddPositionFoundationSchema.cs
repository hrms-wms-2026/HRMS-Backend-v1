using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionFoundationSchema : Migration
    {
        private static readonly string[] TenantTables = ["position_reporting_history", "management_coverage_records"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "positions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                table: "positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "positions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_occupancy",
                table: "positions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "position_type",
                table: "positions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "unique");

            migrationBuilder.AddColumn<Guid>(
                name: "reports_to_position_id",
                table: "positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "management_coverage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    covered_target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    covered_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    covered_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_management_coverage_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_departments_covered_department_",
                        column: x => x.covered_department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_positions_covered_position_id",
                        column: x => x.covered_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_management_coverage_records_positions_owner_position_id",
                        column: x => x.owner_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "position_reporting_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reports_to_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    change_reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_position_reporting_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_position_reporting_history_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_position_reporting_history_positions_reports_to_position_id",
                        column: x => x.reports_to_position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_positions_department_id",
                table: "positions",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_legal_entity_id",
                table: "positions",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_reports_to_position_id",
                table: "positions",
                column: "reports_to_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id",
                table: "positions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_code",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true,
                filter: "code IS NOT NULL AND legal_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_department_id",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "department_id" });

            migrationBuilder.CreateIndex(
                name: "ix_positions_tenant_id_legal_entity_id_name",
                table: "positions",
                columns: new[] { "tenant_id", "legal_entity_id", "name" },
                unique: true,
                filter: "legal_entity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_covered_department_id",
                table: "management_coverage_records",
                column: "covered_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_covered_position_id",
                table: "management_coverage_records",
                column: "covered_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_legal_entity_id",
                table: "management_coverage_records",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_owner_position_id",
                table: "management_coverage_records",
                column: "owner_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_tenant_id",
                table: "management_coverage_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_management_coverage_records_tenant_legal_entity_owner",
                table: "management_coverage_records",
                columns: new[] { "tenant_id", "legal_entity_id", "owner_position_id" });

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_position_id",
                table: "position_reporting_history",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_reports_to_position_id",
                table: "position_reporting_history",
                column: "reports_to_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_tenant_id",
                table: "position_reporting_history",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_position_reporting_history_tenant_position_effective",
                table: "position_reporting_history",
                columns: new[] { "tenant_id", "position_id", "effective_from", "effective_to" });

            migrationBuilder.AddForeignKey(
                name: "fk_positions_departments_department_id",
                table: "positions",
                column: "department_id",
                principalTable: "departments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_positions_legal_entities_legal_entity_id",
                table: "positions",
                column: "legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_positions_positions_reports_to_position_id",
                table: "positions",
                column: "reports_to_position_id",
                principalTable: "positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

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

            migrationBuilder.DropForeignKey(
                name: "fk_positions_departments_department_id",
                table: "positions");

            migrationBuilder.DropForeignKey(
                name: "fk_positions_legal_entities_legal_entity_id",
                table: "positions");

            migrationBuilder.DropForeignKey(
                name: "fk_positions_positions_reports_to_position_id",
                table: "positions");

            migrationBuilder.DropTable(
                name: "management_coverage_records");

            migrationBuilder.DropTable(
                name: "position_reporting_history");

            migrationBuilder.DropIndex(
                name: "ix_positions_department_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_legal_entity_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_reports_to_position_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_tenant_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_tenant_id_legal_entity_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_tenant_id_legal_entity_id_code",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_tenant_id_legal_entity_id_department_id",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "ix_positions_tenant_id_legal_entity_id_name",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "code",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "max_occupancy",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "position_type",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "reports_to_position_id",
                table: "positions");
        }
    }
}
