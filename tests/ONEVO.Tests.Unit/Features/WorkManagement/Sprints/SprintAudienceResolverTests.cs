using Moq;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints;

/// <summary>Moved from the retired SprintAccessServiceTests - the audience query survived the retirement.</summary>
public class SprintAudienceResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ModuleA = Guid.NewGuid();
    private static readonly Guid ModuleB = Guid.NewGuid();

    private readonly Mock<IWorkTaskRepository> _tasks = new();
    private readonly Mock<IProjectMemberRepository> _members = new();

    [Fact]
    public async Task GetAudience_DistinctActiveMembersAcrossTaskModules()
    {
        var sprintId = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var onlyB = Guid.NewGuid();
        _tasks.Setup(x => x.GetBySprintIdAsync(TenantId, sprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkTask>
            {
                new() { Id = Guid.NewGuid(), SprintId = sprintId, ObjectiveId = ModuleA },
                new() { Id = Guid.NewGuid(), SprintId = sprintId, ObjectiveId = ModuleB }
            });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared } });
        _members.Setup(x => x.ListActiveForObjectiveAsync(TenantId, ModuleB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProjectMember> { new() { EmployeeId = shared }, new() { EmployeeId = onlyB } });

        var audience = await new SprintAudienceResolver(_tasks.Object, _members.Object).GetAudienceEmployeeIdsAsync(TenantId, sprintId);

        Assert.Equal(2, audience.Count);
        Assert.Contains(shared, audience);
        Assert.Contains(onlyB, audience);
    }
}
