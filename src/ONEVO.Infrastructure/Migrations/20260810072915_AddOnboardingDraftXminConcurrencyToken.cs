using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingDraftXminConcurrencyToken : Migration
    {
        // Hand-edited to a no-op: this migration exists only to keep the EF model snapshot in
        // sync after mapping the PostgreSQL system column xmin as a shadow concurrency-token
        // property on OnboardingDraft. xmin already exists on every table - there is no column
        // to add or drop.

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
