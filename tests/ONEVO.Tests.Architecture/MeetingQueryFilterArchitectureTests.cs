using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>Meeting occurrences are tasks (task_kind = 'meeting') that ordinary task queries must never see
/// (meetings-as-tasks Phase 2 spec 2.2, 5.1).</summary>
public sealed class MeetingQueryFilterArchitectureTests
{
    [Fact]
    public void WorkTask_DeclaresTheTenantScopeAndExcludeMeetingsFiltersByName()
    {
        using var context = CreateModelInspectionContext();

        var filters = context.Model.FindEntityType(typeof(WorkTask))!.GetDeclaredQueryFilters();

        Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.ExcludeMeetings);
        Assert.Contains(filters, f => f.Key == WorkTaskQueryFilters.TenantScope);
        Assert.DoesNotContain(filters, f => f.Key is null); // never mix anonymous and named filters
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
}
