using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810154000_AddTenantSeatPolicyContract")]
public partial class AddTenantSeatPolicyContract : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "included_seats", table: "tenant_subscriptions", type: "integer", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "overage_allowed", table: "tenant_subscriptions", type: "boolean", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "included_seats", table: "tenant_subscriptions");
        migrationBuilder.DropColumn(name: "overage_allowed", table: "tenant_subscriptions");
    }
}
