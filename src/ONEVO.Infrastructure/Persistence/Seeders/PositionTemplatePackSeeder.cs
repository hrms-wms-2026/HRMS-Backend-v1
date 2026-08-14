using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Boot-time seed for system Position Template Packs (configuration_templates rows with
/// template_type = position_template). Idempotent by template_key. Read/seed-only: this does not
/// create tenant departments/positions and does not write
/// tenant_configuration_template_applications - those happen only when a tenant explicitly applies
/// a pack from the Position screen (a later feature).
///
/// configuration_templates.created_by_id is NOT NULL with a Restrict FK to platform_users, so
/// seeding requires at least one existing platform user. If none exists yet (for example,
/// PlatformBootstrap:SuperAdminEmail is unset), this seeder skips and logs - it does not fail
/// startup and will seed on a later restart once a platform user has been bootstrapped.
///
/// None of the seven packs link a role template: the current global role_templates catalog
/// (RoleTemplateSeeder) only defines "HR Manager" and "Workspace Member", which do not map safely
/// to most of the concrete positions below (CEO, COO, CTO, Team Lead, Project Manager, etc.).
/// linked_role_template_id is left null throughout rather than guessing an incorrect mapping.
/// </summary>
public sealed class PositionTemplatePackSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PositionTemplatePackSeeder> _logger;

    public PositionTemplatePackSeeder(IServiceProvider services, ILogger<PositionTemplatePackSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SeedAsync(db, _logger, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Position template pack seeder could not run because the database or migrated schema " +
                "is unavailable. Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. " +
                "Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        var creator = await db.PlatformUsers
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (creator is null)
        {
            logger.LogInformation(
                "Position template pack seeder skipped: no platform_users row exists yet. System " +
                "position template packs require a valid created_by_id (NOT NULL, Restrict FK to " +
                "platform_users). This seeder will run on a later startup once a platform user has " +
                "been bootstrapped (see PlatformBootstrap:SuperAdminEmail).");
            return;
        }

        var packs = BuildSystemPacks(creator.Id);

        var added = 0;
        foreach (var pack in packs)
        {
            var exists = await db.ConfigurationTemplates.AnyAsync(t => t.TemplateKey == pack.TemplateKey, ct);
            if (exists)
                continue;

            db.ConfigurationTemplates.Add(pack);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} position template pack(s).", added);
        }
        else
        {
            logger.LogInformation("Position template packs already present — skipping seed.");
        }
    }

    private static List<ConfigurationTemplate> BuildSystemPacks(Guid createdById)
    {
        var seededAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const string coreHrOnly = """["core_hr"]""";

        return new List<ConfigurationTemplate>
        {
            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000001"),
                templateKey: "executive-leadership-template",
                name: "Executive Leadership Template",
                description: "Starter C-suite and department head structure.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Executive Leadership Template",
                rangeKey: "101-500",
                min: 101,
                max: 500,
                industry: null,
                positions: new[]
                {
                    ("chief-executive-officer", "CEO / Chief Executive Officer", "Executive", (string?)null),
                    ("chief-operating-officer", "COO / Chief Operating Officer", "Executive", "chief-executive-officer"),
                    ("chief-technology-officer", "CTO / Chief Technology Officer", "Executive", "chief-executive-officer"),
                    ("chief-financial-officer", "CFO / Chief Financial Officer", "Executive", "chief-executive-officer"),
                    ("chro-head-of-people", "CHRO / Head of People", "Executive", "chief-executive-officer"),
                    ("department-director", "Department Head / Director", "Executive", "chief-operating-officer"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000002"),
                templateKey: "hr-people-operations-template",
                name: "HR / People Operations Template",
                description: "Core HR and people operations team structure.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "HR / People Operations Template",
                rangeKey: "51-100",
                min: 51,
                max: 100,
                industry: null,
                positions: new[]
                {
                    ("hr-manager", "HR Manager", "Human Resources", (string?)null),
                    ("hr-business-partner", "HR Business Partner", "Human Resources", "hr-manager"),
                    ("talent-acquisition-specialist", "Talent Acquisition Specialist / Recruiter", "Human Resources", "hr-manager"),
                    ("payroll-specialist", "Payroll Specialist", "Human Resources", "hr-manager"),
                    ("people-operations-coordinator", "People Operations Coordinator", "Human Resources", "hr-manager"),
                    ("learning-development-coordinator", "Training / Learning & Development Coordinator", "Human Resources", "hr-manager"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000003"),
                templateKey: "management-layer-template",
                name: "Management Layer Template",
                description: "General management chain from GM down to supervisor.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Management Layer Template",
                rangeKey: "101-500",
                min: 101,
                max: 500,
                industry: null,
                positions: new[]
                {
                    ("general-manager", "General Manager", "Operations", (string?)null),
                    ("department-manager", "Department Manager", "Operations", "general-manager"),
                    ("operations-manager", "Operations Manager", "Operations", "general-manager"),
                    ("assistant-manager", "Assistant Manager", "Operations", "department-manager"),
                    ("supervisor", "Supervisor", "Operations", "assistant-manager"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000004"),
                templateKey: "team-lead-template",
                name: "Team Lead Template",
                description: "Frontline team and technical lead structure.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Team Lead Template",
                rangeKey: "11-50",
                min: 11,
                max: 50,
                industry: null,
                positions: new[]
                {
                    ("team-lead", "Team Lead", "Engineering", (string?)null),
                    ("technical-lead", "Technical Lead", "Engineering", "team-lead"),
                    ("shift-lead", "Shift Lead", "Operations", "team-lead"),
                    ("senior-individual-contributor", "Senior Software Engineer", "Engineering", "team-lead"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000005"),
                templateKey: "project-delivery-management-template",
                name: "Project / Delivery Management Template",
                description: "Delivery, program, and project management structure.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Project / Delivery Management Template",
                rangeKey: "51-100",
                min: 51,
                max: 100,
                industry: null,
                positions: new[]
                {
                    ("delivery-manager", "Delivery Manager", "Delivery", (string?)null),
                    ("program-manager", "Program Manager", "Delivery", "delivery-manager"),
                    ("project-manager", "Project Manager", "Delivery", "program-manager"),
                    ("scrum-master", "Scrum Master", "Delivery", "project-manager"),
                    ("product-owner", "Product Owner", "Delivery", "project-manager"),
                    ("project-coordinator", "Project Coordinator", "Delivery", "project-manager"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000006"),
                templateKey: "software-engineering-starter-template",
                name: "Software / Engineering Starter Template",
                description: "Small engineering team structure for a software company.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Software / Engineering Starter Template",
                rangeKey: "11-50",
                min: 11,
                max: 50,
                industry: "software",
                positions: new[]
                {
                    ("engineering-manager", "Engineering Manager", "Engineering", (string?)null),
                    ("software-engineer", "Software Engineer", "Engineering", "engineering-manager"),
                    ("qa-engineer", "QA Engineer", "Engineering", "engineering-manager"),
                    ("devops-engineer", "DevOps Engineer", "Engineering", "engineering-manager"),
                    ("ui-ux-designer", "UI/UX Designer", "Engineering", "engineering-manager"),
                }),

            BuildPack(
                id: new Guid("a1000000-0000-4000-8000-000000000007"),
                templateKey: "operations-starter-template",
                name: "Operations Starter Template",
                description: "Small operations and office administration structure.",
                createdById: createdById,
                createdAt: seededAt,
                modulesJson: coreHrOnly,
                packName: "Operations Starter Template",
                rangeKey: "11-50",
                min: 11,
                max: 50,
                industry: null,
                positions: new[]
                {
                    ("operations-manager", "Operations Manager", "Operations", (string?)null),
                    ("operations-executive", "Operations Executive", "Operations", "operations-manager"),
                    ("admin-officer", "Admin Officer", "Operations", "operations-manager"),
                    ("office-coordinator", "Office Coordinator", "Operations", "operations-manager"),
                }),
        };
    }

    private static ConfigurationTemplate BuildPack(
        Guid id,
        string templateKey,
        string name,
        string description,
        Guid createdById,
        DateTimeOffset createdAt,
        string modulesJson,
        string packName,
        string rangeKey,
        int min,
        int? max,
        string? industry,
        (string Key, string Name, string Dept, string? ReportsTo)[] positions)
    {
        var payload = new SeedPayload(
            packName,
            rangeKey,
            min,
            max,
            industry,
            positions
                .Select(p => new SeedPositionPayload(p.Key, p.Name, p.Dept, p.ReportsTo, null))
                .ToList());

        return new ConfigurationTemplate
        {
            Id = id,
            TemplateKey = templateKey,
            TemplateType = ConfigurationTemplate.TypePositionTemplate,
            Name = name,
            Description = description,
            Version = 1,
            ModuleKeysJson = modulesJson,
            IndustryProfileTag = null,
            PayloadJson = JsonSerializer.Serialize(payload),
            IsSystem = true,
            IsActive = true,
            CreatedById = createdById,
            CreatedAt = createdAt
        };
    }

    private sealed record SeedPayload(
        [property: JsonPropertyName("pack_name")] string PackName,
        [property: JsonPropertyName("employee_count_range_key")] string EmployeeCountRangeKey,
        [property: JsonPropertyName("employee_count_min")] int EmployeeCountMin,
        [property: JsonPropertyName("employee_count_max")] int? EmployeeCountMax,
        [property: JsonPropertyName("industry")] string? Industry,
        [property: JsonPropertyName("positions")] List<SeedPositionPayload> Positions);

    private sealed record SeedPositionPayload(
        [property: JsonPropertyName("position_key")] string PositionKey,
        [property: JsonPropertyName("position_name")] string PositionName,
        [property: JsonPropertyName("department_name")] string DepartmentName,
        [property: JsonPropertyName("reports_to_position_key")] string? ReportsToPositionKey,
        [property: JsonPropertyName("linked_role_template_id")] Guid? LinkedRoleTemplateId);
}
