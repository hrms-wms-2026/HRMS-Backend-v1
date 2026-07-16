using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAdminSessionsWithCanonicalPlatformTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_admin_sessions");

            migrationBuilder.CreateTable(
                name: "platform_permissions",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    module_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_high_risk = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_permissions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    google_sub = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mfa_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    invite_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_users_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_auth_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_auth_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_auth_events_platform_users_user_id",
                        column: x => x.user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_roles_platform_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    invite_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_invites_platform_users_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    csrf_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_sessions_platform_users_account_id",
                        column: x => x.account_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    granted_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_role_permissions", x => new { x.role_id, x.permission_code });
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_permissions_permission_c",
                        column: x => x.permission_code,
                        principalTable: "platform_permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_users_granted_by_id",
                        column: x => x.granted_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_users_assigned_by_id",
                        column: x => x.assigned_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_users_user_id",
                        column: x => x.user_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_auth_events_created_at",
                table: "platform_auth_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_auth_events_user_id",
                table: "platform_auth_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_permissions_granted_by_id",
                table: "platform_role_permissions",
                column: "granted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_permissions_permission_code",
                table: "platform_role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_platform_roles_created_by_id",
                table: "platform_roles",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_roles_name",
                table: "platform_roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_email",
                table: "platform_user_invites",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_invite_token_hash",
                table: "platform_user_invites",
                column: "invite_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_invites_invited_by_id",
                table: "platform_user_invites",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_roles_assigned_by_id",
                table: "platform_user_roles",
                column: "assigned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_roles_role_id",
                table: "platform_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_account_id",
                table: "platform_user_sessions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_expires_at",
                table: "platform_user_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_sessions_token_hash",
                table: "platform_user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_created_by_id",
                table: "platform_users",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_invite_status",
                table: "platform_users",
                column: "invite_status");

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_status",
                table: "platform_users",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_auth_events");

            migrationBuilder.DropTable(
                name: "platform_role_permissions");

            migrationBuilder.DropTable(
                name: "platform_user_invites");

            migrationBuilder.DropTable(
                name: "platform_user_roles");

            migrationBuilder.DropTable(
                name: "platform_user_sessions");

            migrationBuilder.DropTable(
                name: "platform_permissions");

            migrationBuilder.DropTable(
                name: "platform_roles");

            migrationBuilder.DropTable(
                name: "platform_users");

            migrationBuilder.CreateTable(
                name: "platform_admin_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    csrf_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    key_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    platform_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_admin_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_sessions_key_hash",
                table: "platform_admin_sessions",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_sessions_platform_user_id_is_revoked",
                table: "platform_admin_sessions",
                columns: new[] { "platform_user_id", "is_revoked" });
        }
    }
}
