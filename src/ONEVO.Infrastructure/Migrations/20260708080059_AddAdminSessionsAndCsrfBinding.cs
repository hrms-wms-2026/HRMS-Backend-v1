using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSessionsAndCsrfBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_levels");

            // NOT a rename: job_level_id (dead — no backing entity, see BACKEND_PHASE1_ARCHITECTURE_AUDIT.md)
            // is dropped outright, and position_id is a distinct, previously-unmigrated column for
            // InvitationToken.PositionId. EF's diff misread these as the same column; keeping them
            // separate avoids repointing any existing job_level_id GUIDs at unrelated position rows.
            migrationBuilder.DropColumn(
                name: "job_level_id",
                table: "invitation_tokens");

            migrationBuilder.AddColumn<Guid>(
                name: "position_id",
                table: "invitation_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "csrf_token_hash",
                table: "sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "platform_admin_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    platform_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    csrf_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_admin_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    default_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_sessions_platform_user_id_is_revoked",
                table: "platform_admin_sessions",
                columns: new[] { "platform_user_id", "is_revoked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_admin_sessions");

            migrationBuilder.DropTable(
                name: "positions");

            migrationBuilder.DropColumn(
                name: "csrf_token_hash",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "position_id",
                table: "invitation_tokens");

            migrationBuilder.AddColumn<Guid>(
                name: "job_level_id",
                table: "invitation_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "job_levels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_levels", x => x.id);
                });
        }
    }
}
