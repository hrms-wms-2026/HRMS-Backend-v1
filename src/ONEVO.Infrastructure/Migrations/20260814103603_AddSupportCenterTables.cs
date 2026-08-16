using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportCenterTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by_platform_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_to_platform_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
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
                });

            migrationBuilder.CreateTable(
                name: "support_ticket_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_platform_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_ticket_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_support_ticket_comments_platform_users_author_platform_user",
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
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_created_at",
                table: "platform_announcements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_is_published",
                table: "platform_announcements",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_platform_announcements_severity",
                table: "platform_announcements",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_author_platform_user_id",
                table: "support_ticket_comments",
                column: "author_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_created_at",
                table: "support_ticket_comments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_support_ticket_comments_ticket_id",
                table: "support_ticket_comments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_assigned_to_platform_user_id",
                table: "support_tickets",
                column: "assigned_to_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_at",
                table: "support_tickets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_by_platform_user_id",
                table: "support_tickets",
                column: "created_by_platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_priority",
                table: "support_tickets",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status",
                table: "support_tickets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_tenant_id",
                table: "support_tickets",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_announcements");

            migrationBuilder.DropTable(
                name: "support_ticket_comments");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
