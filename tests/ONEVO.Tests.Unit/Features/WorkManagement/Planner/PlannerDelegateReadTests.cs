using System.Globalization;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Features.WorkManagement.Planner.Commands;
using ONEVO.Application.Features.WorkManagement.Planner.Contracts;
using ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>Delegate task planning (2026-11-11), "Planner reads": a planning delegate reads the principal's focus and
/// editable days; a meeting-only delegate's view of the days is read-only; blocks say who planned them and carry a version.</summary>
public sealed class PlannerDelegateReadTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 11, 11);
    private static readonly DateOnly To = new(2026, 11, 13);

    private readonly Employee _secretary = Person("Sara", "Fernando");
    private readonly Employee _ceo = Person("Chief", "Executive");
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarTimezoneResolver> _timezones = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IDelegateAccess> _access = new();
    private readonly Mock<IPlannerRepository> _repo = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private static Employee Person(string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = first, LastName = last,
        EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), EmploymentStatusId = EmploymentStatusIds.Active,
    };

    public PlannerDelegateReadTests()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Tenant);
        _user.SetupGet(u => u.UserId).Returns(_secretary.UserId);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(Tenant, _secretary.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary.Id);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        var people = new Dictionary<Guid, Employee> { [_secretary.Id] = _secretary, [_ceo.Id] = _ceo };
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                people.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
        _employees.Setup(e => e.GetByUserIdsAsync(Tenant, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                people.Values.Where(p => ids.Contains(p.UserId)).ToList());
        _timezones.Setup(t => t.ResolveForUserAsync(Tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("UTC");
        _repo.Setup(r => r.ListBlocksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.GetTasksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlannerTaskInfo>());
        _repo.Setup(r => r.ListSessionsAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.ListFocusTasksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private PlannerRequestHandler Sut()
    {
        var planner = new PlannerService(_repo.Object, Mock.Of<IMeetingRepository>(), _timezones.Object, _employees.Object,
            Mock.Of<IUnitOfWork>(), _clock.Object, Options.Create(new PlannerOptions { Enabled = true }));
        return new PlannerRequestHandler(_user.Object, _identity.Object, planner, _delegates.Object, _clock.Object, _access.Object);
    }

    private void SecretaryIs(bool meetingDelegate, bool planning)
    {
        _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, _ceo.Id, _secretary.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(meetingDelegate);
        _access.Setup(a => a.PlanningSubjectAsync(Tenant, _secretary.Id, _ceo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planning ? new DelegateSubject(_ceo.Id, _ceo.UserId) : null);
    }

    private PlannedTaskBlock Block(Guid createdByUserId, DateTimeOffset? updatedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = _ceo.Id, TaskId = Guid.NewGuid(),
        StartAt = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero), EndAt = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero),
        CreatedById = createdByUserId, CreatedAt = Now.AddDays(-1), UpdatedAt = updatedAt,
    };

    private void CeoHasBlocks(params PlannedTaskBlock[] blocks)
        => _repo.Setup(r => r.ListBlocksAsync(Tenant, _ceo.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blocks);

    [Fact]
    public async Task Range_ForAMeetingOnlyDelegate_HasNoEditableDay()
    {
        SecretaryIs(meetingDelegate: true, planning: false);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.All(result.Value!.Days, d => Assert.False(d.Editable));
    }

    [Fact]
    public async Task Range_ForAPlanningDelegate_KeepsThePrincipalsEditableDays()
    {
        SecretaryIs(meetingDelegate: true, planning: true);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(result.Value!.Days, d => d.Editable);
    }

    [Fact]
    public async Task Range_ABlockADelegateWrote_SaysWhoPlannedIt_AndAnOwnBlockDoesNot()
    {
        SecretaryIs(meetingDelegate: true, planning: true);
        var byDelegate = Block(_secretary.UserId);
        var own = Block(_ceo.UserId);
        CeoHasBlocks(byDelegate, own);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo.Id), CancellationToken.None);

        var blocks = result.Value!.Days.SelectMany(d => d.Blocks).ToDictionary(b => b.Id);
        Assert.Equal("Sara Fernando", blocks[byDelegate.Id].PlannedByName);
        Assert.Null(blocks[own.Id].PlannedByName);
    }

    [Fact]
    public async Task Range_Block_CarriesItsVersion()
    {
        SecretaryIs(meetingDelegate: true, planning: true);
        var updated = Block(_ceo.UserId, updatedAt: Now.AddHours(-2));
        var neverUpdated = Block(_ceo.UserId);
        CeoHasBlocks(updated, neverUpdated);

        var result = await Sut().Handle(new GetMyPlannerRangeQuery(From, To, _ceo.Id), CancellationToken.None);

        var blocks = result.Value!.Days.SelectMany(d => d.Blocks).ToDictionary(b => b.Id);
        Assert.Equal(Now.AddHours(-2).ToString("O", CultureInfo.InvariantCulture), blocks[updated.Id].Version);
        Assert.Equal(neverUpdated.CreatedAt.ToString("O", CultureInfo.InvariantCulture), blocks[neverUpdated.Id].Version);
    }

    [Fact]
    public async Task Focus_ForAPlanningDelegate_IsThePrincipals()
    {
        SecretaryIs(meetingDelegate: true, planning: true);

        var result = await Sut().Handle(new GetMyPlannerFocusQuery(_ceo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        _repo.Verify(r => r.ListFocusTasksAsync(Tenant, _ceo.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.ListFocusTasksAsync(Tenant, _secretary.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Focus_ForAMeetingOnlyDelegate_IsNotFound()
    {
        SecretaryIs(meetingDelegate: true, planning: false);

        var result = await Sut().Handle(new GetMyPlannerFocusQuery(_ceo.Id), CancellationToken.None);

        Assert.Equal((404, PlannerErrorCodes.NotFound), (result.StatusCode, result.ErrorCode));
        _repo.Verify(r => r.ListFocusTasksAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Focus_WithoutForEmployeeId_IsMine()
    {
        var result = await Sut().Handle(new GetMyPlannerFocusQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        _repo.Verify(r => r.ListFocusTasksAsync(Tenant, _secretary.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
        _access.VerifyNoOtherCalls();
    }
}
