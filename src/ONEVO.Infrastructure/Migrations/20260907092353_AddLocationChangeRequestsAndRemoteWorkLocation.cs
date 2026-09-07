using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationChangeRequestsAndRemoteWorkLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_office_location",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_radius_meters",
                table: "legal_entities");

            migrationBuilder.AddColumn<bool>(
                name: "remote_location_check_required",
                table: "clock_in_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "employee_work_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_work_locations", x => x.id);
                    table.CheckConstraint("ck_employee_work_locations_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_employee_work_locations_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_employee_work_locations_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_latitude = table.Column<double>(type: "double precision", nullable: false),
                    requested_longitude = table.Column<double>(type: "double precision", nullable: false),
                    requested_accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_change_requests", x => x.id);
                    table.CheckConstraint("ck_location_change_requests_latitude", "requested_latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_location_change_requests_longitude", "requested_longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_location_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_change_requests_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_change_requests_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_office_location",
                table: "legal_entities",
                sql: "(office_latitude IS NULL AND office_longitude IS NULL) OR (office_latitude IS NOT NULL AND office_longitude IS NOT NULL AND office_latitude BETWEEN -90 AND 90 AND office_longitude BETWEEN -180 AND 180)");

            migrationBuilder.CreateIndex(
                name: "ix_employee_work_locations_employee_id",
                table: "employee_work_locations",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_employee_work_locations_tenant_employee",
                table: "employee_work_locations",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_employee_id",
                table: "location_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_legal_entity_id",
                table: "location_change_requests",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_reviewed_by_id",
                table: "location_change_requests",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_tenant_legal_entity_status",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "legal_entity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_location_change_requests_tenant_status",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_location_change_requests_active_employee",
                table: "location_change_requests",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status IN ('pending', 'approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_work_locations");

            migrationBuilder.DropTable(
                name: "location_change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_office_location",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "remote_location_check_required",
                table: "clock_in_policies");

            migrationBuilder.AddColumn<int>(
                name: "office_radius_meters",
                table: "legal_entities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_office_location",
                table: "legal_entities",
                sql: "(office_latitude IS NULL AND office_longitude IS NULL AND office_radius_meters IS NULL) OR (office_latitude IS NOT NULL AND office_longitude IS NOT NULL AND office_radius_meters IS NOT NULL AND office_latitude BETWEEN -90 AND 90 AND office_longitude BETWEEN -180 AND 180 AND office_radius_meters > 0)");
        }
    }
}
