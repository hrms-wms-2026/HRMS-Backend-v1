using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810155000_AddEmployeeOnboardingInvitationContract")]
public partial class AddEmployeeOnboardingInvitationContract : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "purpose", table: "invitation_tokens", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "general");
        migrationBuilder.AddColumn<Guid>(name: "legal_entity_id", table: "invitation_tokens", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "employee_id", table: "invitation_tokens", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "onboarding_draft_id", table: "invitation_tokens", type: "uuid", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "purpose", table: "invitation_tokens");
        migrationBuilder.DropColumn(name: "legal_entity_id", table: "invitation_tokens");
        migrationBuilder.DropColumn(name: "employee_id", table: "invitation_tokens");
        migrationBuilder.DropColumn(name: "onboarding_draft_id", table: "invitation_tokens");
    }
}
