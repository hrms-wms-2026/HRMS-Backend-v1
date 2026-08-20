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

    private HandlerSetup BuildHandler(
        Project? project,
        Guid? callerId = null)
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
        var membership = new Mock<IMilestoneMembershipCoordinator>();
        var invitations = new Mock<IProjectMemberInvitationRepository>();
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
}
