using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.ReactivateUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SignOutUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.Commands.SuspendUserAccount;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Tests.Unit.Features.Auth.UserAccounts;

public class UserAccountsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserAccountRepository _accounts = Substitute.For<IUserAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public UserAccountsTests()
    {
        _currentUser.TenantId.Returns(TenantId);
        _currentUser.UserId.Returns(Admin);
        _clock.UtcNow.Returns(Now);
    }

    private static UserAccountRow Row(
        Guid? userId = null, bool isActive = true, DateTimeOffset? suspendedAt = null,
        int? employmentStatusId = EmploymentStatusIds.Active, bool owner = false,
        DateTimeOffset? inviteExpires = null, DateTimeOffset? inviteUsed = null, DateTimeOffset? inviteRevoked = null,
        int sessions = 0, int devices = 0) =>
        new(userId ?? Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", isActive, suspendedAt, null, Now,
            Guid.NewGuid(), employmentStatusId, ["Employee"], owner, inviteExpires, inviteUsed, inviteRevoked,
            sessions, devices);

    private User Arrange(UserAccountRow row)
    {
        var user = new User { Id = row.UserId, TenantId = TenantId, IsActive = row.IsActive, SuspendedAt = row.SuspendedAt };
        _accounts.GetAsync(TenantId, row.UserId, Now, Arg.Any<CancellationToken>()).Returns(row);
        _users.GetByIdAsync(row.UserId, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Theory]
    [InlineData(true, false, EmploymentStatusIds.Active, null, UserAccountStatus.Active)]
    [InlineData(false, true, EmploymentStatusIds.Active, null, UserAccountStatus.Suspended)]
    [InlineData(false, false, EmploymentStatusIds.Terminated, null, UserAccountStatus.Offboarded)]
    [InlineData(false, true, EmploymentStatusIds.Resigned, null, UserAccountStatus.Offboarded)]
    [InlineData(false, false, EmploymentStatusIds.Active, 1, UserAccountStatus.Invited)]
    [InlineData(false, false, EmploymentStatusIds.Active, -1, UserAccountStatus.InviteExpired)]
    [InlineData(false, false, EmploymentStatusIds.Active, null, UserAccountStatus.Inactive)]
    public void Status_FollowsPrecedence(bool isActive, bool suspended, int employmentStatusId, int? inviteHours, string expected)
    {
        var row = Row(isActive: isActive, suspendedAt: suspended ? Now.AddDays(-1) : null,
            employmentStatusId: employmentStatusId,
            inviteExpires: inviteHours is int h ? Now.AddHours(h) : null);

        UserAccountStatus.Of(row, Now).Should().Be(expected);
    }

    [Fact]
    public void Status_RevokedInvite()
    {
        var row = Row(isActive: false, inviteExpires: Now.AddHours(5), inviteRevoked: Now.AddHours(-1));
        UserAccountStatus.Of(row, Now).Should().Be(UserAccountStatus.InviteRevoked);
    }

    [Fact]
    public void Response_OffersOnlyActionsTheStateAllows()
    {
        var expired = UserAccountStatus.ToResponse(Row(isActive: false, inviteExpires: Now.AddHours(-1)), Admin, Now);
        expired.CanResendInvitation.Should().BeTrue();
        expired.CanRevokeInvitation.Should().BeTrue();
        expired.CanSuspend.Should().BeFalse();

        var pending = UserAccountStatus.ToResponse(Row(isActive: false, inviteExpires: Now.AddHours(1)), Admin, Now);
        pending.CanResendInvitation.Should().BeFalse();
        pending.CanRevokeInvitation.Should().BeTrue();

        var owner = UserAccountStatus.ToResponse(Row(owner: true, sessions: 1), Admin, Now);
        owner.CanSuspend.Should().BeFalse();
        owner.CanSignOut.Should().BeTrue();

        var self = UserAccountStatus.ToResponse(Row(userId: Admin, sessions: 1), Admin, Now);
        self.CanSuspend.Should().BeFalse();
        self.CanSignOut.Should().BeFalse();
        self.IsSelf.Should().BeTrue();
    }

    private SuspendUserAccountCommandHandler Suspend() => new(_users, _accounts, _unitOfWork, _currentUser, _clock);

    [Fact]
    public async Task Suspend_DeactivatesAndEndsAllAccess()
    {
        var row = Row(sessions: 2);
        var user = Arrange(row);

        var result = await Suspend().Handle(new SuspendUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        user.SuspendedAt.Should().Be(Now);
        user.SuspendedById.Should().Be(Admin);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _accounts.Received(1).RevokeAllAccessAsync(TenantId, row.UserId, Now, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_Self_IsRefused()
    {
        var row = Row(userId: Admin);
        var user = Arrange(row);

        var result = await Suspend().Handle(new SuspendUserAccountCommand(Admin), default);

        result.IsSuccess.Should().BeFalse();
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Suspend_Owner_IsForbidden()
    {
        var row = Row(owner: true);
        Arrange(row);

        var result = await Suspend().Handle(new SuspendUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Suspend_PendingInvite_IsRefused()
    {
        var row = Row(isActive: false, inviteExpires: Now.AddHours(3));
        Arrange(row);

        var result = await Suspend().Handle(new SuspendUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeFalse();
        await _accounts.DidNotReceiveWithAnyArgs().RevokeAllAccessAsync(default, default, default, default!, default);
    }

    private ReactivateUserAccountCommandHandler Reactivate() => new(_users, _accounts, _unitOfWork, _currentUser, _clock);

    [Fact]
    public async Task Reactivate_RestoresASuspendedAccount()
    {
        var row = Row(isActive: false, suspendedAt: Now.AddDays(-2));
        var user = Arrange(row);

        var result = await Reactivate().Handle(new ReactivateUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        user.SuspendedAt.Should().BeNull();
    }

    [Fact]
    public async Task Reactivate_NeverTurnsAnOffboardedUserBackOn()
    {
        var row = Row(isActive: false, suspendedAt: Now.AddDays(-2), employmentStatusId: EmploymentStatusIds.Terminated);
        var user = Arrange(row);

        var result = await Reactivate().Handle(new ReactivateUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeFalse();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivate_NeverActivatesAnUnacceptedInvite()
    {
        var row = Row(isActive: false, inviteExpires: Now.AddHours(3));
        var user = Arrange(row);

        var result = await Reactivate().Handle(new ReactivateUserAccountCommand(row.UserId), default);

        result.IsSuccess.Should().BeFalse();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SignOut_RevokesAccess_ButNotForYourself()
    {
        var row = Row(sessions: 1, devices: 1);
        Arrange(row);
        _accounts.RevokeAllAccessAsync(TenantId, row.UserId, Now, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(2);
        var sut = new SignOutUserAccountCommandHandler(_accounts, _currentUser, _clock);

        var result = await sut.Handle(new SignOutUserAccountCommand(row.UserId), default);
        result.IsSuccess.Should().BeTrue();
        result.Value!.EndedCount.Should().Be(2);

        var self = await sut.Handle(new SignOutUserAccountCommand(Admin), default);
        self.IsSuccess.Should().BeFalse();
    }
}
