using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "employment_status",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_mode",
                table: "employees");

            migrationBuilder.AddColumn<int>(
                name: "employment_status_id",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "employment_type_id",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "work_mode_id",
                table: "employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "approval_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_statuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employment_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "severities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_severities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_modes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_modes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_statuses_code",
                table: "approval_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_statuses_code",
                table: "employment_statuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employment_types_code",
                table: "employment_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_severities_code",
                table: "severities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_modes_code",
                table: "work_modes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_statuses");

            migrationBuilder.DropTable(
                name: "employment_statuses");

            migrationBuilder.DropTable(
                name: "employment_types");

            migrationBuilder.DropTable(
                name: "severities");

            migrationBuilder.DropTable(
                name: "work_modes");

            migrationBuilder.DropColumn(
                name: "employment_status_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type_id",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_mode_id",
                table: "employees");

            migrationBuilder.AddColumn<string>(
                name: "employment_status",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                table: "employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "work_mode",
                table: "employees",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }
    }
}
