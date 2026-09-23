using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalEntityOfficeLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "office_address",
                table: "legal_entities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "office_latitude",
                table: "legal_entities",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "office_longitude",
                table: "legal_entities",
                type: "double precision",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_office_location",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_address",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_latitude",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_longitude",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "office_radius_meters",
                table: "legal_entities");
        }
    }
}
