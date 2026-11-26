using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrevoEmailProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "platform_providers",
                columns: new[] { "id", "created_at", "display_name", "is_active", "provider_family", "provider_key", "updated_at" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000013"), new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Brevo", true, "transactional_email", "brevo", new DateTimeOffset(new DateTime(2026, 7, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "platform_providers",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000013"));
        }
    }
}
