using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedPhaseOnePlanModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    address_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_entities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_provisioning_states",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tenant_details_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subscription_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modules_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    roles_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settings_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    owner_invite_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activation_ready = table.Column<bool>(type: "boolean", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_provisioning_states", x => x.tenant_id);
                });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"auth\",\"configuration\",\"roles\",\"notifications\",\"org\",\"core_hr\",\"leave\",\"calendar\",\"monitoring\",\"workforce\",\"verification\",\"exceptions\",\"analytics\",\"work_management\",\"chat\",\"chat_ai\",\"integrations\"]");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id",
                table: "legal_entities",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legal_entities");

            migrationBuilder.DropTable(
                name: "tenant_provisioning_states");

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                column: "included_modules_json",
                value: "[\"core_hr\",\"work_management\"]");
        }
    }
}
