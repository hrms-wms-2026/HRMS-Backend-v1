using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingDrafts : Migration
    {
        private static readonly string[] TenantTables = ["onboarding_drafts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onboarding_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    work_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    selected_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited_tasks_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    draft_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_saved_step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_onboarding_drafts_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_department_id",
                table: "onboarding_drafts",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_legal_entity_id",
                table: "onboarding_drafts",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_position_id",
                table: "onboarding_drafts",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_tenant_id_started_by_id",
                table: "onboarding_drafts",
                columns: new[] { "tenant_id", "started_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_drafts_tenant_id_status",
                table: "onboarding_drafts",
                columns: new[] { "tenant_id", "status" });

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
                name: "onboarding_drafts");
        }
    }
}
