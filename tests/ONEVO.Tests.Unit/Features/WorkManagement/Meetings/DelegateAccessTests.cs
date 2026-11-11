using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Delegate task planning (2026-11-11), "The delegate check": one service, read from the database every call.</summary>
public sealed class DelegateAccessTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly Guid _secretary = Guid.NewGuid();
    private readonly Employee _ceo = new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Chief", LastName = "Executive",
        EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), EmploymentStatusId = EmploymentStatusIds.Active,
    };
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDelegateScopeRepository> _scope = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public DelegateAccessTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ceo);
    }

    private DelegateAccess Sut() => new(_delegates.Object, _scope.Object, _employees.Object, _clock.Object);

    private void Planning(params bool[] answers)
    {
        var setup = _delegates.SetupSequence(d => d.IsActivePlanningDelegateAsync(Tenant, _ceo.Id, _secretary, Now, It.IsAny<CancellationToken>()));
        foreach (var answer in answers)
            setup = setup.ReturnsAsync(answer);
    }

    [Fact]
    public async Task PlanningSubject_ForAnActivePlanningDelegate_IsThePrincipal()
    {
        Planning(true);

        var subject = await Sut().PlanningSubjectAsync(Tenant, _secretary, _ceo.Id, CancellationToken.None);

        Assert.Equal(new DelegateSubject(_ceo.Id, _ceo.UserId), subject);
    }

    [Fact]
    public async Task PlanningSubject_ForAMeetingOnlyDelegateOrStranger_IsNull()
    {
        Planning(false);

        Assert.Null(await Sut().PlanningSubjectAsync(Tenant, _secretary, _ceo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PlanningSubject_ForOneself_IsNull()
    {
        Assert.Null(await Sut().PlanningSubjectAsync(Tenant, _ceo.Id, _ceo.Id, CancellationToken.None));
        _delegates.Verify(d => d.IsActivePlanningDelegateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanningSubject_ReadsTheGrantOnEveryCall()
    {
        Planning(true, false);
        var access = Sut();

        Assert.NotNull(await access.PlanningSubjectAsync(Tenant, _secretary, _ceo.Id, CancellationToken.None));
        Assert.Null(await access.PlanningSubjectAsync(Tenant, _secretary, _ceo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task PlanningSubject_AnEmployeeThatIsGone_IsNull()
    {
        Planning(true);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        Assert.Null(await Sut().PlanningSubjectAsync(Tenant, _secretary, _ceo.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CanReadTaskAndProject_AskTheScopeForThePrincipal()
    {
        var task = Guid.NewGuid();
        var project = Guid.NewGuid();
        _scope.Setup(s => s.IsAssignedOrParentOfAssignedAsync(Tenant, _ceo.Id, task, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _scope.Setup(s => s.HasAssignedTaskInProjectAsync(Tenant, _ceo.Id, project, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await Sut().CanReadTaskAsync(Tenant, _ceo.Id, task, CancellationToken.None));
        Assert.True(await Sut().CanReadProjectAsync(Tenant, _ceo.Id, project, CancellationToken.None));
        Assert.False(await Sut().CanReadTaskAsync(Tenant, _ceo.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await Sut().CanReadProjectAsync(Tenant, _ceo.Id, Guid.NewGuid(), CancellationToken.None));
    }
}
