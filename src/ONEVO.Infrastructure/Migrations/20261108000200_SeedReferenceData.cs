using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Reference data that only the PostgreSQL migrations created, with the values PostgreSQL held
    /// (docs/reports/database/2026-11-07-postgres-reference-data.json). Everything else comes from
    /// the startup seeders.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108000200_SeedReferenceData")]
    public class SeedReferenceData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // module_catalog: 27 rows.
            migrationBuilder.Sql("""
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'activity_monitoring', N'Activity Monitoring', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'analytics', N'Analytics & Reports', N'workforce_intelligence', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107444+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'auth', N'Auth & Security', N'shared', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.105041+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'boards', N'Boards', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'calendar', N'Calendar', N'hr_management', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107397+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'configuration', N'Configuration', N'shared', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.107313+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'core_hr', N'Core HR', N'hr_management', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107377+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'desktop_agent_gateway', N'Desktop Agent Gateway', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'discrepancy_engine', N'Discrepancy Engine', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'exception_engine', N'Exception Engine', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'identity_verification', N'Identity Verification', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'integrations', N'Integrations', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.10746+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'monitoring', N'Activity Monitoring', N'workforce_intelligence', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107427+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'notifications', N'Notifications', N'shared', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.107356+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'objectives_milestones', N'Objectives & Milestones', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'org', N'Org Structure', N'shared', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.107368+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'org_structure', N'Org Structure', N'hr_management', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'planning_sprints', N'Planning & Sprints', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'productivity_analytics', N'Productivity Analytics', N'workforce_intelligence', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'projects', N'Projects', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'roles', N'Roles & Permissions', N'shared', N'phase_1', N'flat_rate', N'[]', 1, N'2026-11-07T07:19:11.107346+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'tasks', N'Tasks', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'time_attendance', N'Time & Attendance', N'hr_management', N'phase_1', N'per_employee', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'time_off', N'Time Off', N'hr_management', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107385+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'verification', N'Identity Verification', N'workforce_intelligence', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107436+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'work_management', N'Work Management', N'worksync', N'phase_1', N'per_employee', N'[]', 1, N'2026-11-07T07:19:11.107452+00:00', NULL, N'[]', 0, 0, N'[]');
                INSERT INTO dbo.module_catalog ([module_key], [name], [pillar], [phase], [pricing_unit], [storage_reference], [is_active], [created_at], [updated_at], [ai_token_reference], [is_ai_enabled], [is_storage_consuming], [pricing_reference]) VALUES (N'worksync_foundation', N'WorkSync Foundation', N'worksync', N'phase_1', N'flat_rate', N'[]', 1, N'2025-01-01T00:00:00+00:00', NULL, N'[]', 0, 0, N'[]');
                """);

            // employment_statuses: 6 rows.
            migrationBuilder.Sql("""
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (1, N'active', N'Active');
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (2, N'on_leave', N'On Leave');
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (3, N'suspended', N'Suspended');
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (4, N'terminated', N'Terminated');
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (5, N'offboarding', N'Offboarding');
                INSERT INTO dbo.employment_statuses ([id], [code], [label]) VALUES (6, N'resigned', N'Resigned');
                """);

            // A later PostgreSQL migration replaced "monitoring"; the HasData value in code predates it.
            migrationBuilder.Sql("""
                UPDATE dbo.subscription_plans SET included_modules_json = N'["org_structure","core_hr","leave","calendar","time_attendance","activity_monitoring","discrepancy_engine","identity_verification","exception_engine","productivity_analytics","desktop_agent_gateway","worksync_foundation","projects","objectives_milestones","tasks","boards","planning_sprints"]'
                WHERE code = N'starter_51_200';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("The SQL Server baseline has no earlier state to return to.");
    }
}
