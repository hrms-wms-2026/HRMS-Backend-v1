using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Dashboard.Team.Sources;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using Xunit;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team.Sources;

public sealed class MonitoringExceptionTeamActionSourceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid LegalEntityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static MonitoringException Exception(
        Guid employeeId, ExceptionStatus status, DateTimeOffset detectedAt, DateTimeOffset? escalatedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Type = ExceptionType.SustainedLowActivity,
        Status = status, Title = "Low activity", DetectedAt = detectedAt, EscalatedAt = escalatedAt,
    };

    [Fact]
    public async Task IsGatedAsync_false_when_scope_resolver_returns_null()
    {
        var currentUser = CurrentUser();
        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(x => x.ListEmployeeIdsWithExceptionsAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var scopeResolver = new Mock<IExceptionScopeResolver>();
        scopeResolver.Setup(x => x.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExceptionScope?)null);
        var source = new MonitoringExceptionTeamActionSource(
            currentUser, scopeResolver.Object, exceptions.Object, Mock.Of<IEmployeeRepository>());

        Assert.False(await source.IsGatedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Actionable_counts_open_and_escalated_together_but_acknowledged_is_reported_separately()
    {
        var currentUser = CurrentUser();
        var employeeId = Guid.NewGuid();
        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(x => x.ListEmployeeIdsWithExceptionsAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([employeeId]);
        var scopeResolver = ScopeResolverReturning(new ExceptionScope(true, null, [employeeId]));

        var open = Exception(employeeId, ExceptionStatus.Open, DateTimeOffset.Parse("2026-09-10T00:00:00Z"));
        var escalated = Exception(employeeId, ExceptionStatus.Escalated, DateTimeOffset.Parse("2026-09-01T00:00:00Z"), DateTimeOffset.Parse("2026-09-05T00:00:00Z"));

        exceptions.Setup(x => x.GetListAsync(
                TenantId, It.Is<ExceptionListFilter>(f => f.Statuses != null && f.Statuses.Contains(ExceptionStatus.Open) && f.Statuses.Contains(ExceptionStatus.Escalated)),
                1, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([open, escalated]);
        exceptions.Setup(x => x.GetListTotalCountAsync(
                TenantId, It.Is<ExceptionListFilter>(f => f.Status == ExceptionStatus.Acknowledged), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [employeeId] = new() { Id = employeeId, FirstName = "Priya", LastName = "K" } });

        var source = new MonitoringExceptionTeamActionSource(currentUser, scopeResolver, exceptions.Object, employees.Object);
        var summary = await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        Assert.Equal(2, summary.PendingCount);
        Assert.Equal(3, summary.InProgressCount);
        // Escalated's EscalatedAt (2026-09-05) predates Open's DetectedAt (2026-09-10) -> escalated sorts first.
        Assert.Equal(escalated.Id, summary.TopItems[0].EntityId);
        Assert.Equal(escalated.EscalatedAt, summary.OldestPendingAt);
        Assert.Equal("Escalated", summary.TopItems[0].ExceptionStatus);
        Assert.Equal("Priya K", summary.TopItems[0].SubjectName);
    }

    [Fact]
    public async Task Hr_scope_passes_null_employeeIds_non_hr_scope_passes_the_scoped_ids()
    {
        var currentUser = CurrentUser();
        var exceptions = new Mock<IExceptionRepository>();
        exceptions.Setup(x => x.ListEmployeeIdsWithExceptionsAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var scopedId = Guid.NewGuid();
        var scopeResolver = ScopeResolverReturning(new ExceptionScope(false, Guid.NewGuid(), [scopedId]));
        exceptions.Setup(x => x.GetListAsync(TenantId, It.IsAny<ExceptionListFilter>(), 1, int.MaxValue, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        exceptions.Setup(x => x.GetListTotalCountAsync(TenantId, It.IsAny<ExceptionListFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var source = new MonitoringExceptionTeamActionSource(currentUser, scopeResolver, exceptions.Object, Mock.Of<IEmployeeRepository>());
        await source.GetSummaryAsync(LegalEntityId, 5, CancellationToken.None);

        exceptions.Verify(x => x.GetListAsync(
            TenantId, It.Is<ExceptionListFilter>(f => f.EmployeeIds != null && f.EmployeeIds.Single() == scopedId),
            1, int.MaxValue, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        return currentUser.Object;
    }

    private static IExceptionScopeResolver ScopeResolverReturning(ExceptionScope scope)
    {
        var resolver = new Mock<IExceptionScopeResolver>();
        resolver.Setup(x => x.ResolveAsync(false, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        return resolver.Object;
    }
}
