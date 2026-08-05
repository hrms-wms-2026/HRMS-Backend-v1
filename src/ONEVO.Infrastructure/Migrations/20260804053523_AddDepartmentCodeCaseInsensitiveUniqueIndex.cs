using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentCodeCaseInsensitiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_departments_tenant_id_legal_entity_id_code",
                table: "departments");

            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    dup_count integer;
                BEGIN
                    SELECT COUNT(*) INTO dup_count
                    FROM (
                        SELECT tenant_id, legal_entity_id, lower(code)
                        FROM departments
                        WHERE code IS NOT NULL
                        GROUP BY tenant_id, legal_entity_id, lower(code)
                        HAVING COUNT(*) > 1
                    ) duplicates;

                    IF dup_count > 0 THEN
                        RAISE EXCEPTION 'Cannot add case-insensitive unique department code index: % duplicate (tenant_id, legal_entity_id, lower(code)) group(s) exist. Resolve duplicate codes before retrying this migration.', dup_count;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_departments_tenant_legal_entity_code_lower
                ON departments (tenant_id, legal_entity_id, lower(code))
                WHERE code IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ux_departments_tenant_legal_entity_code_lower;");

            migrationBuilder.CreateIndex(
                name: "ix_departments_tenant_id_legal_entity_id_code",
                table: "departments",
                columns: new[] { "tenant_id", "legal_entity_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");
        }
    }
}
