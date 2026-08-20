using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.Commands.AddProjectMember;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Lookups;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class AddProjectMemberCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LeadUserId = Guid.NewGuid();
    private static readonly Guid LeadEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();
    private static readonly Guid MemberEmployeeId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid DefaultObjectiveId = Guid.NewGuid();

    [Fact]
    public void AddProjectMemberCommand_ReusesAddObjectiveMemberOutcomeResponse()
    {
        var projectId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        IRequest<Result<AddObjectiveMemberOutcomeResponse>> command = new AddProjectMemberCommand(projectId, employeeId);

        Assert.Equal(projectId, ((AddProjectMemberCommand)command).ProjectId);
        Assert.Equal(employeeId, ((AddProjectMemberCommand)command).EmployeeId);
    }

    private static Project ActiveProject(bool isActive = true) => new()
    {
        Id = ProjectId, TenantId = TenantId, LeadId = LeadEmployeeId, IsActive = isActive,
        Name = "Website Revamp", Identifier = "WEB", CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed record HandlerSetup(
        AddProjectMemberCommandHandler Handler,
        Mock<IProjectMemberInvitationRepository> Invitations,
        Mock<IMilestoneMembershipCoordinator> Membership);

    private static Objective DefaultObjective(bool isAchieved = false) => new()
    {
        Id = DefaultObjectiveId, TenantId = TenantId, ProjectId = ProjectId, IsDefault = true, Title = "Website Revamp",
        OwnerId = LeadEmployeeId, IsActive = true, IsAchieved = isAchieved,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), CreatedAt = DateTimeOffset.UtcNow
    };

    private HandlerSetup BuildHandler(
        Project? project,
        Objective? defaultObjective = null,
        Guid? callerId = null,
        Employee? assignee = null,
        bool explicitNullAssignee = false,
        bool alreadyActiveMember = false,
        ProjectMemberInvitation? existingPendingInvite = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(callerId ?? LeadUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, LeadUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LeadEmployeeId);
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherEmployeeId);

        var projects = new Mock<IProjectRepository>();
        projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var objectives = new Mock<IObjectiveRepository>();
        objectives.Setup(x => x.GetDefaultByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultObjective);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var mockAssignee = explicitNullAssignee ? null
            : assignee ?? new Employee { Id = MemberEmployeeId, TenantId = TenantId, UserId = Guid.NewGuid(), EmploymentStatusId = EmploymentStatusIds.Active };
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, MemberEmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAssignee);
        if (defaultObjective is not null && mockAssignee is not null)
        {
            membership.Setup(x => x.HasActiveMembershipAsync(TenantId, defaultObjective.ProjectId, defaultObjective.Id, mockAssignee.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(alreadyActiveMember);
        }

        var invitations = new Mock<IProjectMemberInvitationRepository>();
        if (defaultObjective is not null && mockAssignee is not null)
        {
            invitations.Setup(x => x.GetPendingForObjectiveAndEmployeeAsync(TenantId, defaultObjective.Id, mockAssignee.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingPendingInvite);
        }

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddProjectMemberCommandHandler(
            currentUser.Object, identity.Object, projects.Object, objectives.Object,
            membership.Object, invitations.Object, unitOfWork.Object);

        return new HandlerSetup(handler, invitations, membership);
    }

    [Fact]
    public async Task Handle_CallerNotProjectOwner_ReturnsForbidden()
    {
        var setup = BuildHandler(ActiveProject(), callerId: OtherUserId);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("Only the project owner can add members.", result.Error);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ReturnsNotFound()
    {
        var setup = BuildHandler(null);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveProject_ReturnsNotFound()
    {
        var setup = BuildHandler(ActiveProject(isActive: false));

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_MissingDefaultObjective_ReturnsFailureWithoutThrowing()
    {
        var setup = BuildHandler(ActiveProject(), defaultObjective: null);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("This project has no default milestone; contact support.", result.Error);
    }

    [Fact]
    public async Task Handle_AlreadyActiveMember_ReturnsAlreadyMemberTrue()
    {
        var setup = BuildHandler(ActiveProject(), DefaultObjective(), alreadyActiveMember: true);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyMember);
        Assert.Null(result.Value.Invitation);
        setup.Invitations.Verify(x => x.AddAsync(It.IsAny<ProjectMemberInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PendingInviteExists_ReturnsConflict()
    {
        var existing = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = DefaultObjectiveId,
            InvitedEmployeeId = MemberEmployeeId, InviteType = ProjectInvitationTypes.Member,
            Status = ProjectInvitationStatuses.Pending
        };
        var setup = BuildHandler(ActiveProject(), DefaultObjective(), existingPendingInvite: existing);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NewInvite_CreatesPendingMemberInvitationOnDefaultObjective()
    {
        var setup = BuildHandler(ActiveProject(), DefaultObjective());

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyMember);
        Assert.NotNull(result.Value.Invitation);
        Assert.Equal(DefaultObjectiveId, result.Value.Invitation!.ObjectiveId);
        Assert.Equal(ProjectInvitationTypes.Member, result.Value.Invitation.InviteType);
        setup.Invitations.Verify(x => x.AddAsync(It.Is<ProjectMemberInvitation>(i =>
            i.ProjectId == ProjectId && i.ObjectiveId == DefaultObjectiveId
            && i.InvitedEmployeeId == MemberEmployeeId
            && i.InviteType == ProjectInvitationTypes.Member && i.Status == ProjectInvitationStatuses.Pending
            && i.InvitedById == LeadEmployeeId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetEmployeeNotActive_ReturnsFailure()
    {
        var setup = BuildHandler(ActiveProject(), DefaultObjective(), explicitNullAssignee: true);

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_DefaultObjectiveAchieved_ReturnsFailure()
    {
        var setup = BuildHandler(ActiveProject(), DefaultObjective(isAchieved: true));

        var result = await setup.Handler.Handle(new AddProjectMemberCommand(ProjectId, MemberEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("Cannot add members to an achieved milestone.", result.Error);
    }
}
