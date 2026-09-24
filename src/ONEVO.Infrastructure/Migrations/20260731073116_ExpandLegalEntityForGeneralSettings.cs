using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandLegalEntityForGeneralSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company_code",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "date_format",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DD MMM YYYY");

            migrationBuilder.AddColumn<string>(
                name: "default_language",
                table: "legal_entities",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en-US");

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "legal_entities",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "financial_year_start_month",
                table: "legal_entities",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "first_day_of_week",
                table: "legal_entities",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_file_id",
                table: "legal_entities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_legal_entity_id",
                table: "legal_entities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "legal_entities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "standard_working_days",
                table: "legal_entities",
                type: "jsonb",
                nullable: false,
                defaultValue: "[1,2,3,4,5]");

            migrationBuilder.AddColumn<string>(
                name: "tax_registration_number",
                table: "legal_entities",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_format",
                table: "legal_entities",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "12h");

            migrationBuilder.AddColumn<string>(
                name: "timezone",
                table: "legal_entities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vat_gst_number",
                table: "legal_entities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "legal_entities",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_logo_file_id",
                table: "legal_entities",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_parent_legal_entity_id",
                table: "legal_entities",
                column: "parent_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_company_code",
                table: "legal_entities",
                columns: new[] { "tenant_id", "company_code" },
                unique: true,
                filter: "company_code IS NOT NULL");

            // Precheck: legal_entities has never had a uniqueness rule on
            // (tenant_id, name), so this migration must not go silently
            // corrupt existing data if two rows already collide. Fail loudly
            // instead, before the unique index is created.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    duplicate_count integer;
                BEGIN
                    SELECT COUNT(*) INTO duplicate_count
                    FROM (
                        SELECT tenant_id, name
                        FROM legal_entities
                        GROUP BY tenant_id, name
                        HAVING COUNT(*) > 1
                    ) AS duplicates;

                    IF duplicate_count > 0 THEN
                        RAISE EXCEPTION 'Cannot add unique index on legal_entities (tenant_id, name): % duplicate (tenant_id, name) pair(s) exist. Resolve duplicate company names within the same tenant before applying this migration.', duplicate_count;
                    END IF;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_name",
                table: "legal_entities",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_tenant_id_registration_number",
                table: "legal_entities",
                columns: new[] { "tenant_id", "registration_number" },
                unique: true,
                filter: "registration_number IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_financial_year_start_month",
                table: "legal_entities",
                sql: "financial_year_start_month BETWEEN 1 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_first_day_of_week",
                table: "legal_entities",
                sql: "first_day_of_week BETWEEN 1 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "ck_legal_entities_time_format",
                table: "legal_entities",
                sql: "time_format IN ('12h', '24h')");

            migrationBuilder.AddForeignKey(
                name: "fk_legal_entities_file_records_logo_file_id",
                table: "legal_entities",
                column: "logo_file_id",
                principalTable: "file_records",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_legal_entities_legal_entities_parent_legal_entity_id",
                table: "legal_entities",
                column: "parent_legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_legal_entities_file_records_logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropForeignKey(
                name: "fk_legal_entities_legal_entities_parent_legal_entity_id",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_parent_legal_entity_id",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_tenant_id_company_code",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_tenant_id_name",
                table: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_legal_entities_tenant_id_registration_number",
                table: "legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_financial_year_start_month",
                table: "legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_first_day_of_week",
                table: "legal_entities");

            migrationBuilder.DropCheckConstraint(
                name: "ck_legal_entities_time_format",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "company_code",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "date_format",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "default_language",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "email",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "financial_year_start_month",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "first_day_of_week",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "logo_file_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "parent_legal_entity_id",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "standard_working_days",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "tax_registration_number",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "time_format",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "timezone",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "vat_gst_number",
                table: "legal_entities");

            migrationBuilder.DropColumn(
                name: "website",
                table: "legal_entities");
        }
    }
}
