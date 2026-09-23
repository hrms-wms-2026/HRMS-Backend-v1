using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAvatarAndLogoToEntityAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO entity_assets
                    (id, tenant_id, owner_type, owner_id, asset_purpose, file_record_id,
                     is_primary, sort_order, created_by_type, created_by_id, created_at, is_deleted)
                SELECT gen_random_uuid(), e.tenant_id, 'employee', e.id, 'employee_avatar',
                       e.avatar_file_id, true, NULL, 'system', e.id, now(), false
                FROM employees e
                WHERE e.avatar_file_id IS NOT NULL
                  AND e.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM entity_assets a
                      WHERE a.tenant_id = e.tenant_id
                        AND a.owner_type = 'employee'
                        AND a.owner_id = e.id
                        AND a.asset_purpose = 'employee_avatar'
                        AND a.is_primary = true
                        AND a.is_deleted = false);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO entity_assets
                    (id, tenant_id, owner_type, owner_id, asset_purpose, file_record_id,
                     is_primary, sort_order, created_by_type, created_by_id, created_at, is_deleted)
                SELECT gen_random_uuid(), le.tenant_id, 'legal_entity', le.id, 'company_logo',
                       le.logo_file_id, true, NULL, 'system', le.id, now(), false
                FROM legal_entities le
                WHERE le.logo_file_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM entity_assets a
                      WHERE a.tenant_id = le.tenant_id
                        AND a.owner_type = 'legal_entity'
                        AND a.owner_id = le.id
                        AND a.asset_purpose = 'company_logo'
                        AND a.is_primary = true
                        AND a.is_deleted = false);
            ");

            migrationBuilder.DropForeignKey(
                name: "fk_legal_entities_file_records_logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "avatar_file_id",
                table: "employees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "logo_file_id",
                table: "legal_entities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "avatar_file_id",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE employees e
                SET avatar_file_id = a.file_record_id
                FROM entity_assets a
                WHERE a.tenant_id = e.tenant_id
                  AND a.owner_type = 'employee'
                  AND a.owner_id = e.id
                  AND a.asset_purpose = 'employee_avatar'
                  AND a.is_primary = true
                  AND a.is_deleted = false;

                UPDATE legal_entities le
                SET logo_file_id = a.file_record_id
                FROM entity_assets a
                WHERE a.tenant_id = le.tenant_id
                  AND a.owner_type = 'legal_entity'
                  AND a.owner_id = le.id
                  AND a.asset_purpose = 'company_logo'
                  AND a.is_primary = true
                  AND a.is_deleted = false;

                DELETE FROM entity_assets
                WHERE owner_type IN ('employee', 'legal_entity');
            ");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_logo_file_id",
                table: "legal_entities",
                column: "logo_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_legal_entities_file_records_logo_file_id",
                table: "legal_entities",
                column: "logo_file_id",
                principalTable: "file_records",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
