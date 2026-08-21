using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>Boot-time seed for built-in (system) role templates. Idempotent by template name.</summary>
public sealed class RoleTemplateSeeder : IHostedService
{
    public const string HrManagerPermissionCodesJson =
        """["attendance:read","employees:read","leave:approve","leave:manage","leave:read"]""";

    public static string[] HrManagerPermissionCodesForTest() =>
        JsonSerializer.Deserialize<string[]>(HrManagerPermissionCodesJson)!;

    private readonly IServiceProvider _services;
    private readonly ILogger<RoleTemplateSeeder> _logger;

    public RoleTemplateSeeder(IServiceProvider services, ILogger<RoleTemplateSeeder> logger)
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
                "Role template seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task SeedAsync(ApplicationDbContext db, ILogger<RoleTemplateSeeder> logger, CancellationToken ct)
    {
        var templates = new[]
        {
            new RoleTemplate
            {
                Id = new Guid("f0000000-0000-4000-8000-000000000001"),
                Name = "HR Manager",
                Description = "Core HR operations: employees, leave, and attendance (read-focused).",
                ModuleKeysJson = """["core_hr","leave"]""",
                PermissionCodesJson = HrManagerPermissionCodesJson,
                IsSystem = true,
                Version = 1,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            new RoleTemplate
            {
                Id = new Guid("f0000000-0000-4000-8000-000000000002"),
                Name = "Workspace Member",
                Description = "Work management: tasks, projects, and wiki (read-focused).",
                ModuleKeysJson = """["work_management"]""",
                PermissionCodesJson = """["projects:read","tasks:read","wiki:read"]""",
                IsSystem = true,
                Version = 1,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var added = 0;
        foreach (var t in templates)
        {
            var exists = await db.RoleTemplates.AnyAsync(x => x.Name == t.Name, ct);
            if (exists)
                continue;

            await db.RoleTemplates.AddAsync(t, ct);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} role template(s).", added);
        }
        else
            logger.LogInformation("Role templates already present — skipping seed.");
    }
}
