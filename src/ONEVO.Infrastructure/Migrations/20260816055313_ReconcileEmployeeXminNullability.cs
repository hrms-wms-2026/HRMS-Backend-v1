using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileEmployeeXminNullability : Migration
    {
        // Hand-edited to a no-op, same as AddOnboardingDraftXminConcurrencyToken.cs: xmin is a
        // PostgreSQL system column, not a real column to ALTER. This migration exists only to
        // keep the EF model snapshot in sync after the "xmin" shadow property on Employee was
        // changed from uint to uint? (EmployeeConfiguration.cs) so DevSmokeTestTenantSeederTests'
        // non-PostgreSQL unit-test schema (via Database.EnsureCreated()) does not emit a NOT NULL
        // constraint on a column no INSERT ever supplies a value for. PostgreSQL's real xmin
        // always has a value regardless of this CLR-side nullability metadata.

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
