using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ModuleManpowerTests
{
    private static readonly Guid RootOwner = Guid.NewGuid();
    private static readonly Guid ChildOwner = Guid.NewGuid();
    private static readonly Guid GrandchildOwner = Guid.NewGuid();
    private static readonly Guid SiblingOwner = Guid.NewGuid();
    private static readonly Guid Shared = Guid.NewGuid();
    private static readonly Guid ChildMember = Guid.NewGuid();
    private static readonly Guid SiblingMember = Guid.NewGuid();

    private static readonly Objective Root = new() { Id = Guid.NewGuid(), OwnerId = RootOwner, IsDefault = true };
    private static readonly Objective Child = new() { Id = Guid.NewGuid(), ParentObjectiveId = Root.Id, OwnerId = ChildOwner };
    private static readonly Objective Grandchild = new() { Id = Guid.NewGuid(), ParentObjectiveId = Child.Id, OwnerId = GrandchildOwner };
    private static readonly Objective Sibling = new() { Id = Guid.NewGuid(), ParentObjectiveId = Root.Id, OwnerId = SiblingOwner };

    private static readonly ProjectModuleTree Tree = new([Root, Child, Grandchild, Sibling]);

    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> Members = new Dictionary<Guid, IReadOnlySet<Guid>>
    {
        [Root.Id] = new HashSet<Guid> { Shared },
        [Child.Id] = new HashSet<Guid> { Shared, ChildMember },
        [Sibling.Id] = new HashSet<Guid> { SiblingMember },
    };

    [Fact]
    public void Module_CountsItsOwnAndEverySubModulesMembersAndOwners_Once()
        // Child: ChildOwner + Shared + ChildMember, plus Grandchild's owner.
        => Assert.Equal(4, ModuleManpower.Count(Tree, Child.Id, Members));

    [Fact]
    public void RootModule_CountsTheWholeTree_WithoutDuplicates()
        // RootOwner, Shared, ChildOwner, ChildMember, GrandchildOwner, SiblingOwner, SiblingMember.
        => Assert.Equal(7, ModuleManpower.Count(Tree, Root.Id, Members));

    [Fact]
    public void LeafModule_IsJustItsOwnPeople()
        => Assert.Equal(1, ModuleManpower.Count(Tree, Grandchild.Id, Members));

    [Fact]
    public void ExtraPeople_AreAddedOnTop_ButNotCountedTwice()
        => Assert.Equal(5, ModuleManpower.Count(Tree, Child.Id, Members, extraEmployeeIds: [ChildMember, Guid.NewGuid()]));
}
