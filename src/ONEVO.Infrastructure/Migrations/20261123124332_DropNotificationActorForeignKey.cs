using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Removes the foreign key (and its index) from wm_notification_log.actor_employee_id. System monitor
    /// alerts store Guid.Empty as the actor (WorkNotificationKinds.Alert), so a key to employees rejects every
    /// alert, and on a database that already holds alerts it makes 20261120015440 (task 39a) fail outright.
    /// 39a no longer creates the key; this migration removes it from databases where the earlier version of
    /// 39a already ran. Both statements are guarded, so it is a no-op everywhere else.
    /// </summary>
    public partial class DropNotificationActorForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.fk_wm_notification_log_employees_actor_employee_id', N'F') IS NOT NULL
                    ALTER TABLE dbo.wm_notification_log DROP CONSTRAINT fk_wm_notification_log_employees_actor_employee_id;
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'ix_wm_notification_log_actor_employee_id'
                             AND object_id = OBJECT_ID(N'dbo.wm_notification_log'))
                    DROP INDEX ix_wm_notification_log_actor_employee_id ON dbo.wm_notification_log;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to restore: 39a no longer creates the key or the index, and re-adding the key would
            // reject the system alerts that use Guid.Empty as the actor.
        }
    }
}
