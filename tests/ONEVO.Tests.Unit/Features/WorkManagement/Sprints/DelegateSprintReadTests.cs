using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Queries.GetProjectSprints;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>Delegate task planning (2026-11-11), "Task form reads — least privilege": GET /projects/{id}/sprints with
/// forEmployeeId reads as the principal for their active planning delegate, only for a project with their task.</summary>
public sealed class DelegateSprintReadTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid PrincipalId = Guid.NewGuid();
    private static readonly Guid PrincipalUserId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private readonly Mock<IProjectMemberRepository> _members = new();
    private readonly Mock<IDelegateAccess> _access = new();

    private GetProjectSprintsQueryHandler Sut(bool planning, bool canReadProject)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.TenantId).Returns(TenantId);
        user.SetupGet(u => u.UserId).Returns(UserId);
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(i => i.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        var projects = new Mock<IProjectRepository>();
        projects.Setup(p => p.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Project { Id = ProjectId, TenantId = TenantId, IsActive = true, Name = "P", Identifier = "P1" });
        _members.Setup(m => m.HasActiveMembershipAsync(TenantId, ProjectId, PrincipalId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _members.Setup(m => m.HasActiveMembershipAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var permissions = new Mock<IPermissionResolver>();
        permissions.Setup(p => p.ResolveAsync(It.IsAny<Guid>(), TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        var sprints = new Mock<ISprintRepository>();
        sprints.Setup(s => s.GetByProjectAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Sprint>());
        _access.Setup(a => a.PlanningSubjectAsync(TenantId, EmployeeId, PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planning ? new DelegateSubject(PrincipalId, PrincipalUserId) : null);
        _access.Setup(a => a.CanReadProjectAsync(TenantId, PrincipalId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(canReadProject);
        return new GetProjectSprintsQueryHandler(user.Object, identity.Object, projects.Object, _members.Object, permissions.Object, sprints.Object,
            delegateAccess: _access.Object);
    }

    [Fact]
    public async Task ForAPlanningDelegate_ThePrincipalsProject_IsReadAsThePrincipal()
    {
        var result = await Sut(planning: true, canReadProject: true).Handle(new GetProjectSprintsQuery(ProjectId, PrincipalId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        _members.Verify(m => m.HasActiveMembershipAsync(TenantId, ProjectId, PrincipalId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ForAMeetingOnlyDelegate_OrAProjectWithoutTheirTask_IsNotFound()
    {
        var meetingOnly = await Sut(planning: false, canReadProject: true).Handle(new GetProjectSprintsQuery(ProjectId, PrincipalId), CancellationToken.None);
        var outOfScope = await Sut(planning: true, canReadProject: false).Handle(new GetProjectSprintsQuery(ProjectId, PrincipalId), CancellationToken.None);

        Assert.Equal(404, meetingOnly.StatusCode);
        Assert.Equal(404, outOfScope.StatusCode);
    }

    [Fact]
    public async Task WithoutForEmployeeId_ANonMemberIsStillRefused()
    {
        var result = await Sut(planning: true, canReadProject: true).Handle(new GetProjectSprintsQuery(ProjectId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        _access.VerifyNoOtherCalls();
    }
}
