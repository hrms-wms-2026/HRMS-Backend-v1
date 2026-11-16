using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Platform support: announcements (platform-level) and tenant-owned support tickets, comments and
    /// attachments under the RLS Tenant rule. Also grants the new tenant permission support:manage to every
    /// existing Owner role; new tenants get it from DefaultRoleSeeder through the configuration module.
    /// </summary>
    public partial class AddPlatformSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    published_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_announcements", x => x.id);
                    table.CheckConstraint("ck_platform_announcements_window", "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_platform_announcements_platform_users_created_by_platform_user_id",
                        column: x => x.created_by_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_announcements_platform_users_published_by_platform_user_id",
                        column: x => x.published_by_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    created_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    assigned_to_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    awaiting_support = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.CheckConstraint("ck_support_tickets_single_creator", "(created_by_platform_user_id IS NULL AND created_by_user_id IS NOT NULL) OR (created_by_platform_user_id IS NOT NULL AND created_by_user_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_support_tickets_platform_users_assigned_to_platform_user_id",
                        column: x => x.assigned_to_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_platform_users_created_by_platform_user_id",
                        column: x => x.created_by_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_tickets_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_internal = table.Column<bool>(type: "bit", nullable: false),
                    uploaded_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_ticket_attachments", x => x.id);
                    table.CheckConstraint("ck_support_ticket_attachments_internal_is_platform", "is_internal = 0 OR uploaded_by_platform_user_id IS NOT NULL");
                    table.CheckConstraint("ck_support_ticket_attachments_single_uploader", "(uploaded_by_platform_user_id IS NULL AND uploaded_by_user_id IS NOT NULL) OR (uploaded_by_platform_user_id IS NOT NULL AND uploaded_by_user_id IS NULL)");
                    table.CheckConstraint("ck_support_ticket_attachments_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_support_ticket_attachments_platform_users_uploaded_by_platform_user_id",
                        column: x => x.uploaded_by_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_ticket_attachments_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_support_ticket_attachments_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    is_internal = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_ticket_comments", x => x.id);
                    table.CheckConstraint("ck_support_ticket_comments_internal_is_platform", "is_internal = 0 OR author_platform_user_id IS NOT NULL");
                    table.CheckConstraint("ck_support_ticket_comments_single_author", "(author_platform_user_id IS NULL AND author_user_id IS NOT NULL) OR (author_platform_user_id IS NOT NULL AND author_user_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_support_ticket_comments_platform_users_author_platform_user_id",
                        column: x => x.author_platform_user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_ticket_comments_support_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_support_ticket_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_created_at",
                table: "platform_announcements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_created_by_platform_user_id",
                table: "platform_announcements",
                column: "created_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_published_audience",
                table: "platform_announcements",
                columns: new[] { "is_published", "audience", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_published_by_platform_user_id",
                table: "platform_announcements",
                column: "published_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_tenant_ticket_created",
                table: "support_ticket_attachments",
                columns: new[] { "tenant_id", "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_ticket_id",
                table: "support_ticket_attachments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_uploaded_by_platform_user_id",
                table: "support_ticket_attachments",
                column: "uploaded_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_attachments_uploaded_by_user_id",
                table: "support_ticket_attachments",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_support_ticket_attachments_object_key",
                table: "support_ticket_attachments",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_author_platform_user_id",
                table: "support_ticket_comments",
                column: "author_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_author_user_id",
                table: "support_ticket_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_tenant_ticket_created",
                table: "support_ticket_comments",
                columns: new[] { "tenant_id", "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_ticket_id",
                table: "support_ticket_comments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_assignee_status",
                table: "support_tickets",
                columns: new[] { "assigned_to_platform_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_awaiting_support",
                table: "support_tickets",
                column: "awaiting_support",
                filter: "[awaiting_support] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_by_platform_user_id",
                table: "support_tickets",
                column: "created_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_by_user_id",
                table: "support_tickets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status_priority_created",
                table: "support_tickets",
                columns: new[] { "status", "priority", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_status_created",
                table: "support_tickets",
                columns: new[] { "tenant_id", "status", "created_at" });

            foreach (var (table, predicate) in SupportTables)
            {
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, predicate));
            }

            // role_permissions and roles are under RLS, which binds every login on SQL Server, so the
            // backfill runs in admin mode and resets it afterwards. PermissionSeeder upserts the same code
            // at startup, so inserting it here first is safe.
            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';

                IF NOT EXISTS (SELECT 1 FROM dbo.permissions WHERE code = N'support:manage')
                    INSERT INTO dbo.permissions (id, code, description, module)
                    VALUES (NEWID(), N'support:manage', N'Raise and follow up support tickets with Crownus support.', N'configuration');

                INSERT INTO dbo.role_permissions (role_id, permission_id, tenant_id)
                SELECT r.id, p.id, r.tenant_id
                FROM dbo.roles r
                CROSS JOIN dbo.permissions p
                WHERE p.code = N'support:manage'
                  AND r.is_system = 1
                  AND r.is_deleted = 0
                  AND r.name IN (N'Owner', N'Tenant Owner')
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.role_permissions rp
                      WHERE rp.role_id = r.id AND rp.permission_id = p.id);

                EXEC sp_set_session_context N'app.tenant_context_mode', N'';
                """);
        }

        private static readonly (string Table, RlsPredicate Predicate)[] SupportTables =
        [
            ("support_tickets", RlsTableRegistry.AddedAfterBaseline["support_tickets"]),
            ("support_ticket_comments", RlsTableRegistry.AddedAfterBaseline["support_ticket_comments"]),
            ("support_ticket_attachments", RlsTableRegistry.AddedAfterBaseline["support_ticket_attachments"]),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in SupportTables)
            {
                migrationBuilder.Sql(RlsSql.DropPolicy(table));
            }

            migrationBuilder.Sql("""
                EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
                DELETE rp FROM dbo.role_permissions rp
                JOIN dbo.permissions p ON p.id = rp.permission_id
                WHERE p.code = N'support:manage';
                DELETE FROM dbo.permissions WHERE code = N'support:manage';
                EXEC sp_set_session_context N'app.tenant_context_mode', N'';
                """);

            migrationBuilder.DropTable(
                name: "platform_announcements");

            migrationBuilder.DropTable(
                name: "support_ticket_attachments");

            migrationBuilder.DropTable(
                name: "support_ticket_comments");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
