using Moq;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

/// <summary>A hierarchy service mock that returns the given Modules as every project's tree. With no
/// Modules the tree is empty, so only direct memberships count - the neutral default for task
/// query tests that don't exercise parent-to-child visibility.</summary>
internal static class WorkHierarchyServiceMocks
{
    public static Mock<IWorkHierarchyService> WithModules(params Objective[] modules)
    {
        var mock = new Mock<IWorkHierarchyService>();
        mock.Setup(x => x.LoadTreeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(modules));
        return mock;
    }

    /// <summary>A child Module under parentId, for building a tree in tests.</summary>
    public static Objective Module(Guid id, Guid? parentId) => new()
    {
        Id = id, ParentObjectiveId = parentId, OwnerId = Guid.NewGuid(), Title = id.ToString()
    };
}
