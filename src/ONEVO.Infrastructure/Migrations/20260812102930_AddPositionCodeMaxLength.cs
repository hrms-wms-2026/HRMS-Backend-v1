using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionCodeMaxLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard instead of truncate: positions.code participates in the unique partial index
            // ix_positions_tenant_id_legal_entity_id_code, so silently truncating existing codes
            // to 5 characters could collide two previously-distinct codes into one and fail the
            // index build with no clear signal. Fail loudly up front instead.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM positions WHERE code IS NOT NULL AND length(code) > 5) THEN
                        RAISE EXCEPTION 'Cannot shorten positions.code to 5 characters: existing row(s) exceed that length. Resolve manually before re-running this migration.';
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "positions",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "positions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(5)",
                oldMaxLength: 5,
                oldNullable: true);
        }
    }
}
