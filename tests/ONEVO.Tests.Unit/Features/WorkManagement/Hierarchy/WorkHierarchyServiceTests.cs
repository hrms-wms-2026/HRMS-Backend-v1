using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Hierarchy;

public class WorkHierarchyServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Lead = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid Cc = Guid.NewGuid();

    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();
    private readonly HashSet<Guid> _active = new();

    public WorkHierarchyServiceTests()
    {
        _membership.Setup(x => x.GetActiveAssigneeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Where(_active.Contains).ToHashSet());
        _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>
            {
                new() { Id = _root, ProjectId = ProjectId, OwnerId = Lead, IsDefault = true },
                new() { Id = _p, ProjectId = ProjectId, ParentObjectiveId = _root, OwnerId = A },
                new() { Id = _c, ProjectId = ProjectId, ParentObjectiveId = _p, OwnerId = Cc },
            });
    }

    private void Active(Guid employeeId)
    {
        _active.Add(employeeId);
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, UserId = Guid.NewGuid() });
    }

    private WorkHierarchyService Build() => new(_objectives.Object, _membership.Object);

    [Fact]
    public async Task FindActiveHolder_ReturnsPositionOwner_WhenActive()
    {
        Active(A);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().Be(A);
    }

    [Fact]
    public async Task FindActiveHolder_InactiveOwner_WalksUpToNextActiveOwner()
    {
        Active(Lead); // A has no active employee record
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().Be(Lead);
    }

    [Fact]
    public async Task FindActiveHolder_NobodyActive_ReturnsNull()
    {
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, Cc)).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveHolders_GivesEachPositionTheSameHolderAsTheSingleLookup_InOneEmployeeRead()
    {
        Active(Lead); // A is inactive, so _p walks up to Lead; _c's own owner Cc is active
        Active(Cc);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);

        var holders = await service.FindActiveHoldersAsync(TenantId, tree, [_p, _c, _root]);

        holders.Should().BeEquivalentTo(new Dictionary<Guid, Guid?> { [_p] = Lead, [_c] = Cc, [_root] = Lead });
        foreach (var position in new[] { _p, _c, _root })
            holders[position].Should().Be(await service.FindActiveHolderAsync(TenantId, tree, position, Guid.Empty));
        _membership.Verify(x => x.GetActiveAssigneeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FindActiveHolders_NobodyActive_GivesNull()
    {
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHoldersAsync(TenantId, tree, [_c]))[_c].Should().BeNull();
    }

    [Fact]
    public async Task FindActiveHolder_SkipsTheExcludedEmployee()
    {
        Active(A);
        Active(Lead);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, A)).Should().Be(Lead);
    }
}
