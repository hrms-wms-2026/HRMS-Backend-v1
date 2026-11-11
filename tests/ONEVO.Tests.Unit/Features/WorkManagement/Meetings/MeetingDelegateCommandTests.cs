using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>Spec "Who manages delegates": each employee grants, lists and revokes only their own delegates.</summary>
public sealed class MeetingDelegateCommandTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Employee _me = Person("Dapi", "Owner");
    private readonly Employee _secretary = Person("Sara", "Fernando");
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Mock<IMeetingDelegateAudit> _audit = new();
    private readonly List<MeetingDelegate> _added = [];

    private static Employee Person(string first, string last, int status = EmploymentStatusIds.Active) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = first, LastName = last,
        EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1), EmploymentStatusId = status,
    };

    public MeetingDelegateCommandTests()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Tenant);
        _user.SetupGet(u => u.UserId).Returns(_me.UserId);
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(Tenant, _me.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_me.Id);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _employees.Setup(e => e.GetByIdAsync(Tenant, _secretary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [_me.Id] = _me, [_secretary.Id] = _secretary });
        _delegates.Setup(d => d.Add(It.IsAny<MeetingDelegate>())).Callback((MeetingDelegate row) => _added.Add(row));
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result<MeetingDelegateView>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<MeetingDelegateView>>> op, CancellationToken ct) => op(ct));
    }

    private MeetingDelegateCommandHandlers Sut() => new(_user.Object, _identity.Object, _delegates.Object, _employees.Object,
        _unitOfWork.Object, _clock.Object, _audit.Object);

    private MeetingDelegate Grant(DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, PrincipalEmployeeId = _me.Id, DelegateEmployeeId = _secretary.Id,
        CreatedByUserId = _me.UserId, CreatedAt = Now.AddDays(-5), ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task Add_CreatesAnActiveGrantForTheCaller()
    {
        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(_added);
        Assert.Equal((Tenant, _me.Id, _secretary.Id, _me.UserId, Now, (DateTimeOffset?)null),
            (row.TenantId, row.PrincipalEmployeeId, row.DelegateEmployeeId, row.CreatedByUserId, row.CreatedAt, row.ExpiresAt));
        Assert.Equal((_secretary.Id, "Sara Fernando", "active"), (result.Value!.EmployeeId, result.Value.Name, result.Value.State));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Add_WithAFutureExpiry_StoresIt()
    {
        var until = Now.AddDays(14);

        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, until), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(until, Assert.Single(_added).ExpiresAt);
        Assert.Equal(until, result.Value!.ExpiresAt);
    }

    [Fact]
    public async Task Add_WithAPastExpiry_Is400MeetingDelegateExpiryInvalid()
    {
        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, Now), CancellationToken.None);

        Assert.Equal((400, MeetingErrorCodes.DelegateExpiryInvalid), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Add_Self_Is400MeetingDelegateSelf()
    {
        var result = await Sut().Handle(new AddMeetingDelegateCommand(_me.Id, null), CancellationToken.None);

        Assert.Equal((400, MeetingErrorCodes.DelegateSelf), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Add_AnInactiveOrUnknownEmployee_Is404()
    {
        var gone = Person("Old", "Hand", EmploymentStatusIds.Terminated);
        _employees.Setup(e => e.GetByIdAsync(Tenant, gone.Id, It.IsAny<CancellationToken>())).ReturnsAsync(gone);

        var inactive = await Sut().Handle(new AddMeetingDelegateCommand(gone.Id, null), CancellationToken.None);
        var unknown = await Sut().Handle(new AddMeetingDelegateCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.Equal((404, MeetingErrorCodes.DelegateEmployeeNotFound), (inactive.StatusCode, inactive.ErrorCode));
        Assert.Equal((404, MeetingErrorCodes.DelegateEmployeeNotFound), (unknown.StatusCode, unknown.ErrorCode));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Add_AnActiveExistingGrant_Is409MeetingDelegateExists()
    {
        _delegates.Setup(d => d.GetUnrevokedTrackedForPairAsync(Tenant, _me.Id, _secretary.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Grant());

        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.DelegateExists), (result.StatusCode, result.ErrorCode));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Add_OverAnExpiredGrant_ReplacesIt()
    {
        var expired = Grant(expiresAt: Now.AddDays(-1));
        _delegates.Setup(d => d.GetUnrevokedTrackedForPairAsync(Tenant, _me.Id, _secretary.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Now, expired.RevokedAt);
        Assert.Single(_added);
        _audit.Verify(a => a.RecordDelegationAsync(expired, MeetingAuditActions.DelegateRevoked, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_SetsRevokedAt_OnlyForTheCallersOwnRow()
    {
        var mine = Grant();
        var someoneElses = Grant();
        someoneElses.PrincipalEmployeeId = Guid.NewGuid();
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, mine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        var ok = await Sut().Handle(new RevokeMeetingDelegateCommand(mine.Id), CancellationToken.None);
        var denied = await Sut().Handle(new RevokeMeetingDelegateCommand(someoneElses.Id), CancellationToken.None);

        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal(Now, mine.RevokedAt);
        Assert.Equal((404, MeetingErrorCodes.DelegateNotFound), (denied.StatusCode, denied.ErrorCode));
        Assert.Null(someoneElses.RevokedAt);
    }

    [Fact]
    public async Task List_MarksExpiredGrants()
    {
        var active = Grant();
        var expired = Grant(expiresAt: Now.AddMinutes(-1));
        _delegates.Setup(d => d.ListUnrevokedForPrincipalAsync(Tenant, _me.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, expired]);

        var result = await Sut().Handle(new ListMyMeetingDelegatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["active", "expired"], result.Value!.Select(v => v.State));
        Assert.All(result.Value!, v => Assert.Equal("Sara Fernando", v.Name));
    }

    [Fact]
    public async Task ListPrincipals_ReturnsOnlyActivePrincipals()
    {
        var boss = Person("Chief", "Executive");
        _delegates.Setup(d => d.ListActivePrincipalsAsync(Tenant, _me.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActivePrincipal(boss.Id, false)]);
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [boss.Id] = boss });

        var result = await Sut().Handle(new ListMyMeetingPrincipalsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([new MeetingPrincipalView(boss.Id, "Chief Executive")], result.Value!);
    }

    [Fact]
    public async Task GrantAndRevoke_EachRecordOneAudit()
    {
        var mine = Grant();
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, mine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);

        await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null), CancellationToken.None);
        await Sut().Handle(new RevokeMeetingDelegateCommand(mine.Id), CancellationToken.None);

        _audit.Verify(a => a.RecordDelegationAsync(It.Is<MeetingDelegate>(r => r == _added[0]), MeetingAuditActions.DelegateGranted,
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.RecordDelegationAsync(mine, MeetingAuditActions.DelegateRevoked, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Delegate task planning (2026-11-11): "Can also plan my tasks", off by default, set by the principal.

    [Fact]
    public async Task AddDelegate_StoresCanPlanTasks()
    {
        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null, CanPlanTasks: true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(Assert.Single(_added).CanPlanTasks);
        Assert.True(result.Value!.CanPlanTasks);
    }

    [Fact]
    public async Task AddDelegate_WithoutCanPlanTasks_IsFalse()
    {
        var result = await Sut().Handle(new AddMeetingDelegateCommand(_secretary.Id, null), CancellationToken.None);

        Assert.False(Assert.Single(_added).CanPlanTasks);
        Assert.False(result.Value!.CanPlanTasks);
    }

    [Fact]
    public async Task UpdateDelegate_ByThePrincipal_ChangesIt_AndAuditsTheOldValue()
    {
        var row = Grant();
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await Sut().Handle(new UpdateMeetingDelegateCommand(row.Id, true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(row.CanPlanTasks);
        Assert.Equal((_secretary.Id, "Sara Fernando", true), (result.Value!.EmployeeId, result.Value.Name, result.Value.CanPlanTasks));
        _audit.Verify(a => a.RecordDelegationChangeAsync(row, false, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDelegate_SomeoneElsesGrant_IsNotFound()
    {
        var row = Grant();
        row.PrincipalEmployeeId = Guid.NewGuid();
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var result = await Sut().Handle(new UpdateMeetingDelegateCommand(row.Id, true), CancellationToken.None);

        Assert.Equal((404, MeetingErrorCodes.DelegateNotFound), (result.StatusCode, result.ErrorCode));
        Assert.False(row.CanPlanTasks);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDelegate_RevokedOrExpired_IsNotFound()
    {
        var expired = Grant(expiresAt: Now.AddMinutes(-1));
        _delegates.Setup(d => d.GetUnrevokedTrackedAsync(Tenant, expired.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expired);

        var revokedResult = await Sut().Handle(new UpdateMeetingDelegateCommand(Guid.NewGuid(), true), CancellationToken.None);
        var expiredResult = await Sut().Handle(new UpdateMeetingDelegateCommand(expired.Id, true), CancellationToken.None);

        Assert.Equal(404, revokedResult.StatusCode);
        Assert.Equal(404, expiredResult.StatusCode);
        Assert.False(expired.CanPlanTasks);
    }

    [Fact]
    public async Task ListPrincipals_CarriesCanPlanTasks()
    {
        _delegates.Setup(d => d.ListActivePrincipalsAsync(Tenant, _me.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActivePrincipal(_secretary.Id, true)]);

        var result = await Sut().Handle(new ListMyMeetingPrincipalsQuery(), CancellationToken.None);

        Assert.Equal([new MeetingPrincipalView(_secretary.Id, "Sara Fernando", true)], result.Value!);
    }
}
