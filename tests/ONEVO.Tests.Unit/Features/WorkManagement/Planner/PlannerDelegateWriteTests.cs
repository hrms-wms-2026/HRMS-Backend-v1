using System.Globalization;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
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

/// <summary>Delegate task planning (2026-11-11), "Planner writes" and "Concurrency": a planning delegate writes the
/// principal's blocks by the principal's rules; anyone else gets 404; a stale expectedVersion gets 409 for everyone.</summary>
public sealed class PlannerDelegateWriteTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlotStart = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SlotEnd = SlotStart.AddHours(1);

    private readonly Employee _secretary = Person("Sara", "Fernando");
    private readonly Employee _ceo = Person("Chief", "Executive");
    private readonly Guid _task = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ICalendarTimezoneResolver> _timezones = new();
    private readonly Mock<IDelegateAccess> _access = new();
    private readonly Mock<IPlannerRepository> _repo = new() { DefaultValue = DefaultValue.Empty };
    private readonly Mock<IMeetingRepository> _meetings = new() { DefaultValue = DefaultValue.Empty };
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IPlannerDelegateNotifier> _notifier = new();
    private readonly List<PlannedTaskBlock> _added = [];
    private readonly List<PlannedTaskBlock> _removed = [];

    private static Employee Person(string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = first, LastName = last,
        EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), EmploymentStatusId = EmploymentStatusIds.Active,
    };

    public PlannerDelegateWriteTests()
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
            .ReturnsAsync([]);
        _timezones.Setup(t => t.ResolveForUserAsync(Tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync("UTC");
        _repo.Setup(r => r.GetTasksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PlannerTaskInfo>());
        TaskAssignedTo(_ceo.Id);
        // The day the write returns is composed from these.
        _repo.Setup(r => r.ListBlocksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.ListSessionsAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.ListDueAssignedTasksAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _meetings.Setup(m => m.ListEmployeeDayMeetingsAsync(Tenant, It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _repo.Setup(r => r.AddBlock(It.IsAny<PlannedTaskBlock>())).Callback((PlannedTaskBlock b) => _added.Add(b));
        _repo.Setup(r => r.RemoveBlock(It.IsAny<PlannedTaskBlock>())).Callback((PlannedTaskBlock b) => _removed.Add(b));
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<PlannerDayResponse>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<PlannerDayResponse>>> op, CancellationToken ct) => op(ct));
        SecretaryPlans(true);
    }

    private PlannerRequestHandler Sut()
    {
        var planner = new PlannerService(_repo.Object, _meetings.Object, _timezones.Object, _employees.Object,
            _unitOfWork.Object, _clock.Object, Options.Create(new PlannerOptions { Enabled = true }), _notifier.Object);
        return new PlannerRequestHandler(_user.Object, _identity.Object, planner, Mock.Of<IMeetingDelegateRepository>(), _clock.Object,
            _access.Object);
    }

    private void SecretaryPlans(bool planning)
        => _access.Setup(a => a.PlanningSubjectAsync(Tenant, _secretary.Id, _ceo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planning ? new DelegateSubject(_ceo.Id, _ceo.UserId) : null);

    private void TaskAssignedTo(Guid employeeId)
    {
        foreach (var person in new[] { _ceo.Id, _secretary.Id })
        {
            var info = new PlannerTaskInfo(_task, "Board pack", null, 2m, IsMeeting: false, AssignedToEmployee: person == employeeId, Guid.NewGuid());
            _repo.Setup(r => r.GetTasksAsync(Tenant, person, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_task)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, PlannerTaskInfo> { [_task] = info });
        }
    }

    private PlannedTaskBlock ExistingBlock(Guid employeeId, Guid createdByUserId)
    {
        var block = new PlannedTaskBlock
        {
            Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = employeeId, TaskId = _task, StartAt = SlotStart, EndAt = SlotEnd,
            CreatedById = createdByUserId, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddHours(-1),
        };
        _repo.Setup(r => r.GetBlockTrackedAsync(Tenant, employeeId, block.Id, It.IsAny<CancellationToken>())).ReturnsAsync(block);
        return block;
    }

    private static string VersionOf(PlannedTaskBlock b) => (b.UpdatedAt ?? b.CreatedAt).ToString("O", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Create_ByAPlanningDelegate_LandsOnThePrincipal()
    {
        var result = await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var block = Assert.Single(_added);
        Assert.Equal((_ceo.Id, _task), (block.EmployeeId, block.TaskId));
        _repo.Verify(r => r.LockPlanAsync(Tenant, _ceo.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ByAPlanningDelegate_FollowsThePrincipalsRules()
    {
        TaskAssignedTo(_secretary.Id);
        var notTheirs = await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id),
            CancellationToken.None);
        TaskAssignedTo(_ceo.Id);
        var pastDay = await Sut().Handle(new CreatePlannedBlockCommand(
            new CreatePlannedBlockRequest(_task, SlotStart.AddDays(-2), SlotEnd.AddDays(-2)), _ceo.Id), CancellationToken.None);
        _repo.Setup(r => r.OverlapsAsync(Tenant, _ceo.Id, SlotStart, SlotEnd, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var overlap = await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id),
            CancellationToken.None);

        Assert.Equal(PlannerErrorCodes.TaskNotPlannable, notTheirs.ErrorCode);
        Assert.Equal(PlannerErrorCodes.DayReadOnly, pastDay.ErrorCode);
        Assert.Equal(PlannerErrorCodes.BlockOverlap, overlap.ErrorCode);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Writes_ByAMeetingOnlyDelegate_AreNotFound()
    {
        SecretaryPlans(false);
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        var create = await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id),
            CancellationToken.None);
        var move = await Sut().Handle(new MovePlannedBlockCommand(block.Id, new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(2)),
            _ceo.Id), CancellationToken.None);
        var delete = await Sut().Handle(new DeletePlannedBlockCommand(block.Id, _ceo.Id), CancellationToken.None);

        Assert.All(new[] { create, move, delete }, r => Assert.Equal((404, PlannerErrorCodes.NotFound), (r.StatusCode, r.ErrorCode)));
        Assert.Empty(_added);
        Assert.Empty(_removed);
        Assert.Equal(SlotStart, block.StartAt);
    }

    [Fact]
    public async Task Move_ByADelegate_MovesTheBlockThePrincipalMade()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        var result = await Sut().Handle(new MovePlannedBlockCommand(block.Id,
            new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(2), VersionOf(block)), _ceo.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SlotStart.AddHours(2), block.StartAt);
    }

    [Fact]
    public async Task Move_WithAStaleExpectedVersion_Is409_AndLeavesTheBlock()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);
        var stale = Now.AddHours(-3).ToString("O", CultureInfo.InvariantCulture);

        var result = await Sut().Handle(new MovePlannedBlockCommand(block.Id,
            new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(2), stale), _ceo.Id), CancellationToken.None);

        Assert.Equal((409, PlannerErrorCodes.BlockChanged), (result.StatusCode, result.ErrorCode));
        Assert.Equal("This block was changed by someone else. Refresh and try again.", result.Error);
        Assert.Equal(SlotStart, block.StartAt);
    }

    [Fact]
    public async Task Delete_WithAStaleExpectedVersion_Is409_AndKeepsTheBlock()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        var result = await Sut().Handle(new DeletePlannedBlockCommand(block.Id, _ceo.Id, Now.AddHours(-3).ToString("O", CultureInfo.InvariantCulture)),
            CancellationToken.None);

        Assert.Equal((409, PlannerErrorCodes.BlockChanged), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_removed);
    }

    [Fact]
    public async Task Delete_WithTheCurrentVersion_Removes()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        var result = await Sut().Handle(new DeletePlannedBlockCommand(block.Id, _ceo.Id, VersionOf(block)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([block], _removed);
    }

    [Fact]
    public async Task OwnMove_WithAStaleVersion_Is409_AndWithoutAVersion_StillWorks()
    {
        var block = ExistingBlock(_secretary.Id, _secretary.UserId);
        var stale = Now.AddHours(-3).ToString("O", CultureInfo.InvariantCulture);

        var refused = await Sut().Handle(new MovePlannedBlockCommand(block.Id,
            new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(2), stale)), CancellationToken.None);
        var moved = await Sut().Handle(new MovePlannedBlockCommand(block.Id,
            new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(2))), CancellationToken.None);

        Assert.Equal(409, refused.StatusCode);
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(SlotStart.AddHours(2), block.StartAt);
    }

    // "Notifications": one notice to the principal per delegated write; none for their own.

    private void VerifyNotice(string action, DateTimeOffset start, DateTimeOffset end)
        => _notifier.Verify(n => n.BlockWrittenAsync(Tenant, _ceo.Id, _secretary.Id, action, It.IsAny<Guid>(), "Board pack", start, end,
            It.IsAny<CancellationToken>()), Times.Once);

    [Fact]
    public async Task ADelegatedCreate_NotifiesThePrincipalOnce()
    {
        await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id), CancellationToken.None);

        VerifyNotice(PlannerDelegateActions.Planned, SlotStart, SlotEnd);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ADelegatedMove_NotifiesWithTheNewTime()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        await Sut().Handle(new MovePlannedBlockCommand(block.Id, new MovePlannedBlockRequest(SlotStart.AddHours(2), SlotEnd.AddHours(3)), _ceo.Id),
            CancellationToken.None);

        VerifyNotice(PlannerDelegateActions.Moved, SlotStart.AddHours(2), SlotEnd.AddHours(3));
    }

    [Fact]
    public async Task ADelegatedRemove_NotifiesWithTheRemovedTime()
    {
        var block = ExistingBlock(_ceo.Id, _ceo.UserId);

        await Sut().Handle(new DeletePlannedBlockCommand(block.Id, _ceo.Id), CancellationToken.None);

        VerifyNotice(PlannerDelegateActions.Removed, SlotStart, SlotEnd);
    }

    [Fact]
    public async Task AnOwnWrite_OrARefusedDelegatedWrite_SendsNoNotice()
    {
        TaskAssignedTo(_secretary.Id);
        await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd)), CancellationToken.None);
        await Sut().Handle(new CreatePlannedBlockCommand(new CreatePlannedBlockRequest(_task, SlotStart, SlotEnd), _ceo.Id), CancellationToken.None);

        _notifier.VerifyNoOtherCalls();
    }
}
