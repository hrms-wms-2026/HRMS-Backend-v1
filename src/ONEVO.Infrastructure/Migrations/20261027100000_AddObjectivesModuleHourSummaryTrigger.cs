using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <summary>
/// wm_module_hour_summaries was only refreshed by the tasks triggers, and the refresh reads the Module row. EF has no
/// task → objective foreign key, so when a Module and its tasks are added in one SaveChanges the tasks can be inserted
/// first: their refresh finds no Module and writes nothing, leaving Team Progress empty for that Module. An insert
/// trigger on objectives refreshes the row once the Module exists, so the summary no longer depends on insert order.
/// Re-runs the backfill for Modules the gap already left without a row.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027100000_AddObjectivesModuleHourSummaryTrigger")]
public partial class AddObjectivesModuleHourSummaryTrigger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION wm_objectives_refresh_module_hour_summary() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                PERFORM wm_refresh_module_hour_summary(NEW.id);
                RETURN NULL;
            END;
            $fn$;

            CREATE TRIGGER trg_objectives_module_hour_summary_insert
                AFTER INSERT ON objectives
                FOR EACH ROW EXECUTE FUNCTION wm_objectives_refresh_module_hour_summary();
            """);

        // Admin mode, local to this transaction: objectives and the summary table are under FORCE RLS.
        migrationBuilder.Sql("""
            SELECT set_config('app.tenant_context_mode', 'admin', true);
            SELECT wm_refresh_module_hour_summary(o.id)
            FROM objectives o
            WHERE NOT EXISTS (SELECT 1 FROM wm_module_hour_summaries s WHERE s.id = o.id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_objectives_module_hour_summary_insert ON objectives;
            DROP FUNCTION IF EXISTS wm_objectives_refresh_module_hour_summary();
            """);
    }
}
