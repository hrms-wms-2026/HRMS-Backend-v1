using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>Adds the metadata-only platform provider catalog.</summary>
    public partial class AddPlatformProviderCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_family = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_providers", x => x.id);
                    table.UniqueConstraint("ak_platform_providers_provider_key", x => x.provider_key);
                    table.CheckConstraint("ck_platform_providers_provider_family", "provider_family IN ('oauth_app', 'transactional_email', 'infrastructure', 'object_storage', 'ai_verification', 'payment_gateway')");
                });

            migrationBuilder.InsertData(
                table: "platform_providers",
                columns: new[] { "id", "created_at", "display_name", "is_active", "provider_family", "provider_key", "updated_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Google", true, "oauth_app", "google", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "GitHub", true, "oauth_app", "github", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Microsoft", true, "oauth_app", "microsoft", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Zoom", true, "oauth_app", "zoom", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SendGrid", true, "transactional_email", "sendgrid", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Resend", true, "transactional_email", "resend", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Cloudflare", true, "infrastructure", "cloudflare", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Cloudflare R2", true, "object_storage", "cloudflare_r2", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "AWS Rekognition", true, "ai_verification", "aws_rekognition", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Stripe", true, "payment_gateway", "stripe", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PayHere", true, "payment_gateway", "payhere", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Paddle", true, "payment_gateway", "paddle", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_gateway_configs_provider",
                table: "payment_gateway_configs",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "ix_platform_providers_provider_family_is_active_display_name",
                table: "platform_providers",
                columns: new[] { "provider_family", "is_active", "display_name" });

            migrationBuilder.AddForeignKey(
                name: "fk_payment_gateway_configs_platform_providers_provider",
                table: "payment_gateway_configs",
                column: "provider",
                principalTable: "platform_providers",
                principalColumn: "provider_key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_oauth_apps_platform_providers_provider",
                table: "platform_oauth_apps",
                column: "provider",
                principalTable: "platform_providers",
                principalColumn: "provider_key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_platform_service_keys_platform_providers_service_key",
                table: "platform_service_keys",
                column: "service_key",
                principalTable: "platform_providers",
                principalColumn: "provider_key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_gateway_configs_platform_providers_provider",
                table: "payment_gateway_configs");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_oauth_apps_platform_providers_provider",
                table: "platform_oauth_apps");

            migrationBuilder.DropForeignKey(
                name: "fk_platform_service_keys_platform_providers_service_key",
                table: "platform_service_keys");

            migrationBuilder.DropTable(
                name: "platform_providers");

            migrationBuilder.DropIndex(
                name: "ix_payment_gateway_configs_provider",
                table: "payment_gateway_configs");
        }
    }
}
