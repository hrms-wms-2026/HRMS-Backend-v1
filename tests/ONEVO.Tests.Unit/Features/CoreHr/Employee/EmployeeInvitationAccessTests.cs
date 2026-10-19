using FluentAssertions;
using NSubstitute;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using DraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmployeeInvitationAccessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Adder = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private readonly IOnboardingDraftRepository _drafts = Substitute.For<IOnboardingDraftRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private EmployeeInvitationAccess CreateSut() => new(_drafts, _currentUser);

    private InvitationToken InvitationFromDraft(Guid startedById)
    {
        var draftId = Guid.NewGuid();
        _drafts.GetTrackedAsync(TenantId, draftId, Arg.Any<CancellationToken>())
            .Returns(new DraftEntity { Id = draftId, TenantId = TenantId, StartedById = startedById });
        // Access-grant flow: the approver issued the token, not the person who added the employee.
        return new InvitationToken { TenantId = TenantId, OnboardingDraftId = draftId, CreatedById = Approver };
    }

    private void SignedInAs(Guid userId, params string[] permissions)
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(userId);
        _currentUser.HasPermission(Arg.Any<string>()).Returns(c => permissions.Contains(c.Arg<string>()));
    }

    [Fact]
    public async Task PersonWhoAddedTheEmployee_CanManage_EvenWhenAnApproverIssuedTheToken()
    {
        var invitation = InvitationFromDraft(Adder);
        SignedInAs(Adder);

        (await CreateSut().CanManageAsync(invitation, employeeCreatedById: Approver)).Should().BeTrue();
    }

    [Fact]
    public async Task Approver_WithoutUsersManage_CannotManage()
    {
        var invitation = InvitationFromDraft(Adder);
        SignedInAs(Approver);

        (await CreateSut().CanManageAsync(invitation, employeeCreatedById: Approver)).Should().BeFalse();
    }

    [Fact]
    public async Task AnyoneWithUsersManage_CanManage()
    {
        var invitation = InvitationFromDraft(Adder);
        SignedInAs(Guid.NewGuid(), "users:manage");

        (await CreateSut().CanManageAsync(invitation, employeeCreatedById: Approver)).Should().BeTrue();
    }

    [Fact]
    public async Task UsersReadAlone_IsNotEnough()
    {
        var invitation = InvitationFromDraft(Adder);
        SignedInAs(Guid.NewGuid(), "users:read");

        (await CreateSut().CanManageAsync(invitation, employeeCreatedById: Approver)).Should().BeFalse();
    }

    [Fact]
    public async Task WithoutADraft_FallsBackToEmployeeCreator()
    {
        var invitation = new InvitationToken { TenantId = TenantId, OnboardingDraftId = null };

        (await CreateSut().ResolveInviterUserIdAsync(invitation, employeeCreatedById: Adder)).Should().Be(Adder);
    }

    [Fact]
    public async Task Unauthenticated_CannotManage()
    {
        var invitation = InvitationFromDraft(Adder);
        _currentUser.IsAuthenticated.Returns(false);
        _currentUser.UserId.Returns(Adder);

        (await CreateSut().CanManageAsync(invitation, employeeCreatedById: Adder)).Should().BeFalse();
    }
}
