using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Snapshot-sync only. The hand-written 20260522000001_FixAndSeedAllPhaseOneModuleCatalog
    /// migration already applied these module_catalog / subscription_plans data changes but
    /// did not update ApplicationDbContextModelSnapshot, leaving the model with pending
    /// changes. This migration carries the snapshot update; it must not repeat the data
    /// operations or a fresh database would hit duplicate-key errors.
    /// </summary>
    public partial class SyncModelDrift : Migration
    {
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
