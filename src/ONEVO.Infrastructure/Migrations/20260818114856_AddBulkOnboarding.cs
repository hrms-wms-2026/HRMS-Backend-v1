using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_onboarding_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_employment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    default_work_mode_id = table.Column<int>(type: "integer", nullable: true),
                    default_checklist_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    column_mapping_json = table.Column<string>(type: "jsonb", nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: true),
                    invalid_rows = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_onboarding_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_onboarding_batches_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulk_onboarding_batch_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    raw_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    resolved_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    onboarding_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_onboarding_batch_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_onboarding_batch_rows_bulk_onboarding_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "bulk_onboarding_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bulk_onboarding_batch_rows_onboarding_drafts_onboarding_dra",
                        column: x => x.onboarding_draft_id,
                        principalTable: "onboarding_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_batch_id",
                table: "bulk_onboarding_batch_rows",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_onboarding_draft_id",
                table: "bulk_onboarding_batch_rows",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_tenant_id_batch_id_row_number",
                table: "bulk_onboarding_batch_rows",
                columns: new[] { "tenant_id", "batch_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batch_rows_tenant_id_status",
                table: "bulk_onboarding_batch_rows",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_legal_entity_id",
                table: "bulk_onboarding_batches",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_onboarding_batches_tenant_id_status",
                table: "bulk_onboarding_batches",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_onboarding_batch_rows");

            migrationBuilder.DropTable(
                name: "bulk_onboarding_batches");
        }
    }
}
