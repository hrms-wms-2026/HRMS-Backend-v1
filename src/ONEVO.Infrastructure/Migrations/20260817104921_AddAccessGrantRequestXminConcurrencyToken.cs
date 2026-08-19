using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessGrantRequestXminConcurrencyToken : Migration
    {
        // Hand-edited to a no-op: this migration exists only to keep the EF model snapshot in
        // sync after mapping the PostgreSQL system column xmin as a shadow concurrency-token
        // property on AccessGrantRequest. xmin already exists on every table - there is no column
        // to add or drop. See AddOnboardingDraftXminConcurrencyToken for the identical precedent.

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
