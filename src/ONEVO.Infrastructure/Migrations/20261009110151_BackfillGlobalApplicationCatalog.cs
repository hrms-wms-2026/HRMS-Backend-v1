using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillGlobalApplicationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // app_usage_snapshots is protected by FORCE ROW LEVEL SECURITY. The
            // migrator role owns the table but still needs the existing admin
            // context to read historical rows across every tenant.
            migrationBuilder.Sql(
                """
                SELECT set_config('app.tenant_context_mode', 'admin', true);

                INSERT INTO global_app_catalog
                    (id, app_name, process_name, is_public, is_productive_default,
                     first_seen_at, last_seen_at, created_at)
                SELECT
                    gen_random_uuid(),
                    regexp_replace(observed.process_name, '\.exe$', '', 'i'),
                    observed.process_name,
                    TRUE,
                    FALSE,
                    observed.first_seen_at,
                    observed.last_seen_at,
                    observed.first_seen_at
                FROM
                (
                    SELECT
                        lower(btrim(process_name)) AS process_name,
                        min(captured_at) AS first_seen_at,
                        max(captured_at) AS last_seen_at
                    FROM app_usage_snapshots
                    WHERE process_name IS NOT NULL
                      AND btrim(process_name) <> ''
                    GROUP BY lower(btrim(process_name))
                ) AS observed
                ON CONFLICT (process_name) DO UPDATE
                SET first_seen_at = LEAST(
                        global_app_catalog.first_seen_at,
                        EXCLUDED.first_seen_at),
                    last_seen_at = GREATEST(
                        global_app_catalog.last_seen_at,
                        EXCLUDED.last_seen_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible. Rows may have been categorized or had
            // logos uploaded after this backfill, so deleting them on rollback
            // would destroy administrator-managed data.
        }
    }
}
