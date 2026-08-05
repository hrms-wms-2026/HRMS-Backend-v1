using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgModuleToStarterPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NO-OP (corrected 2026-08-03, same day, before this migration ever shipped -
            // it was never applied outside local Testcontainers/dev runs). This migration's
            // original premise was wrong: it assumed the seeded "starter_51_200" plan still
            // had included_modules_json = ["core_hr","work_management"] (the InitialSchema,
            // 20260509213212, literal insert value) and was missing "org". In fact
            // 20260510103730_SeedPhaseOnePlanModules already updated this same row to the
            // full 17-module list - including "org" - two months earlier. The original Up()
            // here overwrote included_modules_json with a truncated
            // ["core_hr","work_management","org"] literal, silently dropping 14 modules that
            // were already present (auth, configuration, roles, notifications, leave,
            // calendar, monitoring, workforce, verification, exceptions, analytics,
            // work_management already there is fine, chat, chat_ai, integrations). Losing
            // the "roles" module meant DefaultRoleSeeder/ModuleEntitlementService
            // (GetEntitledPermissionsAsync filters Permissions by p.Module) stopped granting
            // "roles:read"/"roles:manage" to any tenant's Owner role provisioned on this
            // plan - the exact cause of TenantProvisioningE2ETests' 403 on
            // GET /api/v1/roles (see TENANT_PROVISIONING_ROLES_READ_FIX_REPORT.md).
            // "org" was never actually missing, so no seed-data change belongs in this
            // migration at all; it is kept (rather than deleted) only so its migration id
            // stays stable for any environment that already recorded it in
            // __EFMigrationsHistory.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: Up() no longer changes included_modules_json, so there is
            // nothing for Down() to revert.
        }
    }
}
