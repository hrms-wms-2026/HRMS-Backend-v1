using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkManagementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_by_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_assets_file_records_file_record_id",
                        column: x => x.file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owning_legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    identifier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    next_task_number = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    description = table.Column<string>(type: "text", nullable: true),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    actual_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    allocated_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    completed_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_legal_entities_owning_legal_entity_id",
                        column: x => x.owning_legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "version_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_version_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labels", x => x.id);
                    table.ForeignKey(
                        name: "fk_labels_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "objectives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_objective_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    progress = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    actual_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    allocated_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    completed_hours = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_objectives", x => x.id);
                    table.ForeignKey(
                        name: "fk_objectives_objectives_parent_objective_id",
                        column: x => x.parent_objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_objectives_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_versions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_versions_version_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "version_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_member_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invited_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_member_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_member_invitations_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_member_invitations_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    objective_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_members_objectives_objective_id",
                        column: x => x.objective_id,
                        principalTable: "objectives",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reminder_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_calendar", x => x.id);
                    table.ForeignKey(
                        name: "fk_release_calendar_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_release_calendar_versions_version_id",
                        column: x => x.version_id,
                        principalTable: "versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_assets_file_record_id",
                table: "entity_assets",
                column: "file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_assets_owner_type_owner_id",
                table: "entity_assets",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_labels_project_id",
                table: "labels",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_labels_tenant_id_project_id_name",
                table: "labels",
                columns: new[] { "tenant_id", "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_objectives_one_default_per_project",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id" },
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_parent_objective_id",
                table: "objectives",
                column: "parent_objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_project_id",
                table: "objectives",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_owner_id_is_active",
                table: "objectives",
                columns: new[] { "tenant_id", "owner_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_objectives_tenant_id_project_id_parent_objective_id",
                table: "objectives",
                columns: new[] { "tenant_id", "project_id", "parent_objective_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_categories_tenant_id_name",
                table: "project_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_objective_id",
                table: "project_member_invitations",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "project_id", "objective_id", "invited_user_id" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_project_id",
                table: "project_member_invitations",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_tenant_invited_user_status",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "invited_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_objective_id",
                table: "project_members",
                column: "objective_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_id",
                table: "project_members",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_tenant_project_objective_active",
                table: "project_members",
                columns: new[] { "tenant_id", "project_id", "objective_id", "is_active" });

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
                name: "ix_projects_owning_legal_entity_id",
                table: "projects",
                column: "owning_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_category_id_is_active",
                table: "projects",
                columns: new[] { "tenant_id", "category_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_identifier",
                table: "projects",
                columns: new[] { "tenant_id", "identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id_owning_legal_entity_id_updated_at",
                table: "projects",
                columns: new[] { "tenant_id", "owning_legal_entity_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_one_active_project_release",
                table: "release_calendar",
                columns: new[] { "version_id", "recipient_user_id" },
                unique: true,
                filter: "is_active = true AND reminder_type = 'project_release'");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_project_id",
                table: "release_calendar",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_release_calendar_tenant_recipient_scheduled_active",
                table: "release_calendar",
                columns: new[] { "tenant_id", "recipient_user_id", "scheduled_date", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_version_statuses_code",
                table: "version_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versions_project_id",
                table: "versions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_versions_status_id",
                table: "versions",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_versions_tenant_id_project_id_status_id",
                table: "versions",
                columns: new[] { "tenant_id", "project_id", "status_id" });

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

        // version_statuses is a global (not tenant-scoped) lookup and is excluded.
        private static readonly string[] TenantTables =
        [
            "entity_assets", "project_categories", "projects", "objectives",
            "project_members", "project_member_invitations", "versions",
            "release_calendar", "labels"
        ];

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
                name: "entity_assets");

            migrationBuilder.DropTable(
                name: "labels");

            migrationBuilder.DropTable(
                name: "project_categories");

            migrationBuilder.DropTable(
                name: "project_member_invitations");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "release_calendar");

            migrationBuilder.DropTable(
                name: "objectives");

            migrationBuilder.DropTable(
                name: "versions");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "version_statuses");
        }
    }
}
