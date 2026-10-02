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

    public WorkHierarchyServiceTests()
    {
        _objectives.Setup(x => x.GetAllByProjectIdAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>
            {
                new() { Id = _root, ProjectId = ProjectId, OwnerId = Lead, IsDefault = true },
                new() { Id = _p, ProjectId = ProjectId, ParentObjectiveId = _root, OwnerId = A },
                new() { Id = _c, ProjectId = ProjectId, ParentObjectiveId = _p, OwnerId = Cc },
            });
    }

    private void Active(Guid employeeId)
        => _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, UserId = Guid.NewGuid() });

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
    public async Task FindActiveHolder_SkipsTheExcludedEmployee()
    {
        Active(A);
        Active(Lead);
        var service = Build();
        var tree = await service.LoadTreeAsync(TenantId, ProjectId);
        (await service.FindActiveHolderAsync(TenantId, tree, _p, A)).Should().Be(Lead);
    }
}
