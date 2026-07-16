using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewaySystemConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_gateway_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    public_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    merchant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_country_routes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    country_name_snapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    gateway_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_country_routes", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_gateway_country_routes_payment_gateway_configs_gate",
                        column: x => x.gateway_config_id,
                        principalTable: "payment_gateway_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_gateway_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_gateway_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    webhook_secret_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    encryption_key_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    credential_version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    rotated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_gateway_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_gateway_credentials_payment_gateway_configs_payment",
                        column: x => x.payment_gateway_config_id,
                        principalTable: "payment_gateway_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_configs_gateway_key",
                table: "payment_gateway_configs",
                column: "gateway_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_country_routes_country_code_environment_is_",
                table: "payment_gateway_country_routes",
                columns: new[] { "country_code", "environment", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_country_routes_gateway_config_id",
                table: "payment_gateway_country_routes",
                column: "gateway_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_credentials_payment_gateway_config_id_is_ac",
                table: "payment_gateway_credentials",
                columns: new[] { "payment_gateway_config_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_gateway_country_routes");

            migrationBuilder.DropTable(
                name: "payment_gateway_credentials");

            migrationBuilder.DropTable(
                name: "payment_gateway_configs");
        }
    }
}
