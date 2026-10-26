using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceAlertDeductionConcurrencyToken : Migration
    {
        // Hand-edited to a no-op, as ReconcileEmployeeXminNullability: xmin is a PostgreSQL system column, not a
        // real column to add. This migration only keeps the model snapshot in sync with the concurrency token.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
