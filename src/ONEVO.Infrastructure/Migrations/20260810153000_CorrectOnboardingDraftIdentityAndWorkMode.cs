using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260810153000_CorrectOnboardingDraftIdentityAndWorkMode")]
    public partial class CorrectOnboardingDraftIdentityAndWorkMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "is_active", table: "work_modes", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<string>(name: "first_name", table: "onboarding_drafts", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "last_name", table: "onboarding_drafts", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<int>(name: "work_mode_id", table: "onboarding_drafts", type: "integer", nullable: false, defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE onboarding_drafts
                SET first_name = (regexp_split_to_array(btrim(employee_name), '\\s+'))[1],
                    last_name = array_to_string((regexp_split_to_array(btrim(employee_name), '\\s+'))[2:array_length(regexp_split_to_array(btrim(employee_name), '\\s+'), 1)], ' ')
                WHERE btrim(employee_name) ~ '\\s+';
                """);
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM onboarding_drafts WHERE first_name IS NULL OR last_name IS NULL OR btrim(first_name) = '' OR btrim(last_name) = '') THEN
                        RAISE EXCEPTION 'Cannot migrate onboarding_drafts.employee_name with fewer than two name parts. Correct the data before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(name: "first_name", table: "onboarding_drafts", type: "character varying(100)", maxLength: 100, nullable: false, oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "last_name", table: "onboarding_drafts", type: "character varying(100)", maxLength: 100, nullable: false, oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);
            migrationBuilder.DropColumn(name: "employee_name", table: "onboarding_drafts");
            migrationBuilder.DropColumn(name: "schedule_id", table: "onboarding_drafts");
            migrationBuilder.CreateIndex(name: "ix_onboarding_drafts_work_mode_id", table: "onboarding_drafts", column: "work_mode_id");
            migrationBuilder.AddForeignKey(name: "fk_onboarding_drafts_work_modes_work_mode_id", table: "onboarding_drafts", column: "work_mode_id", principalTable: "work_modes", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_onboarding_drafts_work_modes_work_mode_id", table: "onboarding_drafts");
            migrationBuilder.DropIndex(name: "ix_onboarding_drafts_work_mode_id", table: "onboarding_drafts");
            migrationBuilder.AddColumn<string>(name: "employee_name", table: "onboarding_drafts", type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: "");
            migrationBuilder.Sql("UPDATE onboarding_drafts SET employee_name = first_name || ' ' || last_name;");
            migrationBuilder.AddColumn<Guid>(name: "schedule_id", table: "onboarding_drafts", type: "uuid", nullable: true);
            migrationBuilder.DropColumn(name: "first_name", table: "onboarding_drafts");
            migrationBuilder.DropColumn(name: "last_name", table: "onboarding_drafts");
            migrationBuilder.DropColumn(name: "work_mode_id", table: "onboarding_drafts");
            migrationBuilder.DropColumn(name: "is_active", table: "work_modes");
        }
    }
}
