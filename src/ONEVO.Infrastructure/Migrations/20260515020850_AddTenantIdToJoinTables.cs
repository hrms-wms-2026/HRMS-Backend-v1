using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdToJoinTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "user_roles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE user_roles ur
                SET tenant_id = u.tenant_id
                FROM users u
                WHERE ur.user_id = u.id;
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "user_mfa",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE user_mfa um
                SET tenant_id = u.tenant_id
                FROM users u
                WHERE um.user_id = u.id;
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "role_permissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE role_permissions rp
                SET tenant_id = r.tenant_id
                FROM roles r
                WHERE rp.role_id = r.id;
            ");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE refresh_tokens rt
                SET tenant_id = u.tenant_id
                FROM users u
                WHERE rt.user_id = u.id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "user_mfa");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "role_permissions");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "refresh_tokens");
        }
    }
}
