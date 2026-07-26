using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginWorkspaceSelectionChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_workspace_selection_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    candidate_workspaces_json = table.Column<string>(type: "jsonb", nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "workspace_selection"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_workspace_selection_challenges", x => x.id);
                    table.CheckConstraint("ck_login_workspace_selection_challenges_failed_attempt_count", "failed_attempt_count BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_login_workspace_selection_challenges_purpose", "purpose = 'workspace_selection'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_challenge_hash",
                table: "login_workspace_selection_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_expires_at",
                table: "login_workspace_selection_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_login_workspace_selection_challenges_normalized_email_creat",
                table: "login_workspace_selection_challenges",
                columns: new[] { "normalized_email", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_workspace_selection_challenges");
        }
    }
}
