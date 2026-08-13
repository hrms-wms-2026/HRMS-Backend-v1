using System.Reflection;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

public class InactivityCaptureAttemptArchitectureTests
{
    [Fact]
    public void Entity_implements_ITenantOwnedEntity()
    {
        Assert.True(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(InactivityCaptureAttempt)));
    }

    [Fact]
    public void Configuration_declares_tenant_employee_prompted_index_and_restrictive_evidence_fk()
    {
        using var context = CreateModelInspectionContext();
        var entity = context.Model.FindEntityType(typeof(InactivityCaptureAttempt));
        Assert.NotNull(entity);

        var index = entity!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_inactivity_capture_attempts_tenant_employee_prompted");
        Assert.Equal(
            [nameof(InactivityCaptureAttempt.TenantId), nameof(InactivityCaptureAttempt.EmployeeId), nameof(InactivityCaptureAttempt.PromptedAt)],
            index.Properties.Select(p => p.Name));

        var idProperty = entity.FindProperty(nameof(InactivityCaptureAttempt.Id));
        Assert.False(idProperty!.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd);

        var evidenceFk = entity.GetForeignKeys()
            .Single(fk => fk.Properties.Any(p => p.Name == nameof(InactivityCaptureAttempt.EvidenceAssetId)));
        Assert.Equal(DeleteBehavior.Restrict, evidenceFk.DeleteBehavior);
    }

    [Fact]
    public void Migration_contains_rls_policy_for_inactivity_capture_attempts()
    {
        var migrationDir = FindMigrationsDirectory();

        var migrationText = Directory.EnumerateFiles(migrationDir, "*AddInactivityCaptureAttempts*.cs")
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("inactivity_capture_attempts", StringComparison.Ordinal));

        Assert.NotNull(migrationText);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", migrationText, StringComparison.Ordinal);
        Assert.Contains("FORCE ROW LEVEL SECURITY", migrationText, StringComparison.Ordinal);
        Assert.Contains("CREATE POLICY tenant_isolation ON inactivity_capture_attempts", migrationText, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"arch-test-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ONEVO.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate src/ONEVO.Infrastructure/Migrations above " + AppContext.BaseDirectory);
    }
}
