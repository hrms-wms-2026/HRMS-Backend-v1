using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMemberInvitationTypeAndLeaderUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invite_type",
                table: "project_member_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "member");

            migrationBuilder.CreateIndex(
                name: "ix_project_member_invitations_one_pending_leader",
                table: "project_member_invitations",
                columns: new[] { "tenant_id", "objective_id" },
                unique: true,
                filter: "status = 'pending' AND invite_type = 'leader'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_member_invitations_one_pending_leader",
                table: "project_member_invitations");

            migrationBuilder.DropColumn(
                name: "invite_type",
                table: "project_member_invitations");
        }
    }
}
