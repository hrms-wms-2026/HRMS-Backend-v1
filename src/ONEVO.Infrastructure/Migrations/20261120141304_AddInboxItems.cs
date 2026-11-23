using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Task 37: the shared inbox header (inbox_items) and its approval chain (inbox_item_assignees), both under the
    /// RLS Tenant rule. Rows are written by the modules (leave first) and backfilled by InboxEntryConsistencyJob.
    /// </summary>
    public partial class AddInboxItems : Migration
    {
        private static readonly string[] InboxTables = ["inbox_items", "inbox_item_assignees"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    subject_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    decided_by_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_items", x => x.id);
                    table.CheckConstraint("ck_inbox_items_category", "category IN ('request', 'alert', 'invitation')");
                    table.CheckConstraint("ck_inbox_items_status", "status IN ('pending', 'approved', 'rejected', 'cancelled', 'resolved')");
                });

            migrationBuilder.CreateTable(
                name: "inbox_item_assignees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    inbox_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sequence_order = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_item_assignees", x => x.id);
                    table.CheckConstraint("ck_inbox_item_assignees_status", "status IN ('pending', 'waiting', 'approved', 'rejected', 'skipped', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_inbox_item_assignees_inbox_entries_inbox_entry_id",
                        column: x => x.inbox_entry_id,
                        principalTable: "inbox_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_item_assignees_tenant_id_employee_id_status_updated_at",
                table: "inbox_item_assignees",
                columns: new[] { "tenant_id", "employee_id", "status", "updated_at" },
                descending: new[] { false, false, false, true })
                .Annotation("SqlServer:Include", new[] { "source", "kind", "inbox_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_item_assignees_inbox_entry_id_employee_id",
                table: "inbox_item_assignees",
                columns: new[] { "inbox_entry_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_items_tenant_id_requested_by_status_updated_at",
                table: "inbox_items",
                columns: new[] { "tenant_id", "requested_by_employee_id", "status", "updated_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_inbox_items_tenant_id_source_kind_subject_id",
                table: "inbox_items",
                columns: new[] { "tenant_id", "source", "kind", "subject_id" },
                unique: true);

            foreach (var table in InboxTables)
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, RlsPredicate.Tenant));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in InboxTables)
                migrationBuilder.Sql(RlsSql.DropPolicy(table));

            migrationBuilder.DropTable(
                name: "inbox_item_assignees");

            migrationBuilder.DropTable(
                name: "inbox_items");
        }
    }
}
