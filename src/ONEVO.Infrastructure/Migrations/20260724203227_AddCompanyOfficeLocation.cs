using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyOfficeLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "office_address_label",
                table: "legal_entities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "office_allowed_radius_meters",
                table: "legal_entities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "office_latitude",
                table: "legal_entities",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "office_longitude",
                table: "legal_entities",
                type: "numeric(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "timezone",
                table: "legal_entities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_office_coordinates",
                table: "legal_entities",
                sql: "(office_latitude IS NULL AND office_longitude IS NULL) OR (office_latitude BETWEEN -90 AND 90 AND office_longitude BETWEEN -180 AND 180)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_office_radius",
                table: "legal_entities",
                sql: "office_allowed_radius_meters IS NULL OR office_allowed_radius_meters BETWEEN 25 AND 50000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_timezone",
                table: "legal_entities",
                sql: "length(trim(timezone)) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_office_coordinates",
                table: "legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_office_radius",
                table: "legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_timezone",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_address_label",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_allowed_radius_meters",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_latitude",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_longitude",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "timezone",
                table: "legal_entities");
        }
    }
}
