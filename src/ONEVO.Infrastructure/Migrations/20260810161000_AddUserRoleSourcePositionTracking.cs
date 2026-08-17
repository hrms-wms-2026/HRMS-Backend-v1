using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <summary>Adds source-position tracking to user_roles so a role generated from a position's
/// access template (RequiresApproval = false at finalization) can be distinguished from a
/// directly-assigned role. Both columns are nullable: null for every directly-assigned role.</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810161000_AddUserRoleSourcePositionTracking")]
public partial class AddUserRoleSourcePositionTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "source_position_id",
            table: "user_roles",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "source_position_access_template_id",
            table: "user_roles",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_user_roles_source_position_access_template_id",
            table: "user_roles",
            column: "source_position_access_template_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_user_roles_source_position_access_template_id",
            table: "user_roles");

        migrationBuilder.DropColumn(
            name: "source_position_access_template_id",
            table: "user_roles");

        migrationBuilder.DropColumn(
            name: "source_position_id",
            table: "user_roles");
    }
}
