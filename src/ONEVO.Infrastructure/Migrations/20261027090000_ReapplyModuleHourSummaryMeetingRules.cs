using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Re-applies the module hour summary's meeting rules on every database. A database that ran Plan D before
    /// development's AddModuleHourSummaries (20261020074523) existed recorded ExcludeMeetingTasksFromModuleHourSummaries
    /// as applied while it did nothing, then received the unfixed function and an update trigger without the task_kind
    /// watch. AddMeetingHoursStamping replaced the function but not the trigger. This puts both back (the meeting-hours
    /// function and the task_kind-watching trigger) and backfills. No model change.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261027090000_ReapplyModuleHourSummaryMeetingRules")]
    public partial class ReapplyModuleHourSummaryMeetingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.ReplaceRefreshFunction(
                AddMeetingHoursStamping.RefreshFunctionWithMeetingHours));
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.ReplaceUpdateTrigger(
                ExcludeMeetingTasksFromModuleHourSummaries.UpdateTriggerWatchingTaskKind));
            migrationBuilder.Sql(ExcludeMeetingTasksFromModuleHourSummaries.BackfillAllModules());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: Up only restores what the earlier migrations already meant to install.
        }
    }
}
