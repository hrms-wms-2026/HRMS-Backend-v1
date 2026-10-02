using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.CoreHr.Entities; // Employee lives here despite its CoreHr/Employee/Entities folder
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public class ModuleWriteServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ParentId = Guid.NewGuid();
    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly Guid HeadId = Guid.NewGuid();
    private static readonly Guid ParentOwnerId = Guid.NewGuid();
    private static readonly Guid NewHeadId = Guid.NewGuid();

    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<ISprintRepository> _sprints = new();
    private readonly Mock<IMilestoneMembershipCoordinator> _membership = new();
    private readonly Mock<IObjectiveAllocationSlackCalculator> _slack = new();

    private readonly Objective _parent = new()
    {
        Id = ParentId, TenantId = TenantId, ProjectId = ProjectId, Title = "Parent", OwnerId = ParentOwnerId, IsActive = true,
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), AllocatedHours = 100m
    };

    public ModuleWriteServiceTests()
    {
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ParentId, It.IsAny<CancellationToken>())).ReturnsAsync(_parent);
        _objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective>());
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, HeadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = HeadId, UserId = Guid.NewGuid() });
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, NewHeadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = NewHeadId, UserId = Guid.NewGuid() });
    }

    private ModuleWriteService Build() => new(_objectives.Object, _sprints.Object, _membership.Object, _slack.Object);

    private static Objective Module(bool isDefault = false, bool isActive = true, bool isAchieved = false) => new()
    {
        Id = ModuleId, TenantId = TenantId, ProjectId = ProjectId, ParentObjectiveId = isDefault ? null : ParentId,
        Title = "Module", OwnerId = HeadId, ReportingManagerId = ParentOwnerId, IsDefault = isDefault, IsActive = isActive,
        IsAchieved = isAchieved, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 3, 1), AllocatedHours = 10m
    };

    private static ModuleEditInput Edit(decimal hours = 20m, DateOnly? end = null)
        => new("  New title  ", "  desc  ", new DateOnly(2026, 2, 1), end ?? new DateOnly(2026, 4, 1), hours);

    [Fact]
    public async Task ValidateEdit_DefaultModule_Fails()
    {
        var result = await Build().ValidateEditAsync(TenantId, Module(isDefault: true), Edit());

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("Use the Project edit endpoint for the Default Objective.");
    }

    [Fact]
    public async Task ValidateEdit_ExceedsParent_Conflict()
    {
        var result = await Build().ValidateEditAsync(TenantId, Module(), Edit(hours: 500m));

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("The edited date range or allocated hours would exceed the parent milestone's.");
    }

    [Fact]
    public async Task ApplyEdit_SetsFieldsAndUpdatedAt()
    {
        var module = Module();

        var result = await Build().ApplyEditAsync(TenantId, module, Edit());

        result.IsSuccess.Should().BeTrue();
        module.Title.Should().Be("New title");
        module.Description.Should().Be("desc");
        module.EndDate.Should().Be(new DateOnly(2026, 4, 1));
        module.AllocatedHours.Should().Be(20m);
        module.UpdatedAt.Should().NotBeNull();
        _objectives.Verify(x => x.Update(module), Times.Once);
    }

    [Fact]
    public async Task ValidateDelete_AlreadyInactive_Conflict()
    {
        var result = await Build().ValidateDeleteAsync(TenantId, Module(isActive: false));

        result.StatusCode.Should().Be(409);
        result.Error.Should().Be("Objective already deleted.");
    }

    [Fact]
    public async Task ApplyTransfer_SwapsOwner_RepointsChildren_MovesMembership()
    {
        var module = Module();
        var child = new Objective { Id = Guid.NewGuid(), ReportingManagerId = HeadId, IsActive = true };
        _objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { child });

        var result = await Build().ApplyTransferAsync(TenantId, module, new ModuleTransferInput(NewHeadId));

        result.IsSuccess.Should().BeTrue();
        module.OwnerId.Should().Be(NewHeadId);
        child.ReportingManagerId.Should().Be(NewHeadId);
        _membership.Verify(x => x.UpsertMembershipAsync(TenantId, ProjectId, ModuleId, NewHeadId, It.IsAny<CancellationToken>()), Times.Once);
        _membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ModuleId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateTransfer_InactiveNewHead_Fails()
    {
        var result = await Build().ValidateTransferAsync(TenantId, Module(), new ModuleTransferInput(Guid.NewGuid()));

        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("The new head must be an active employee in this tenant.");
    }

    [Fact]
    public async Task ValidateAchieve_UnachievedChild_Fails()
    {
        _objectives.Setup(x => x.GetTrackedActiveDirectChildrenAsync(TenantId, ModuleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Objective> { new() { Id = Guid.NewGuid(), IsActive = true, IsAchieved = false } });

        var result = await Build().ValidateAchieveAsync(TenantId, Module());

        result.StatusCode.Should().Be(400);
        result.Error.Should().Be("All sub-milestones must be achieved before this one can be.");
    }

    [Fact]
    public async Task ValidateAchieve_TasksInActiveSprint_Fails()
    {
        _sprints.Setup(x => x.AnyActiveContainingObjectiveTasksAsync(TenantId, ModuleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Build().ValidateAchieveAsync(TenantId, Module());

        result.Error.Should().Be("Tasks of this milestone are still in an Active sprint - complete that sprint first.");
    }

    [Fact]
    public async Task ApplyAchieve_SetsAchievedAndDeactivatesHeadMembership()
    {
        var module = Module();

        var result = await Build().ApplyAchieveAsync(TenantId, module);

        result.IsSuccess.Should().BeTrue();
        module.IsAchieved.Should().BeTrue();
        module.AchievedAt.Should().NotBeNull();
        _membership.Verify(x => x.DeactivateMembershipAsync(TenantId, ProjectId, ModuleId, HeadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyUnachieve_InactiveHead_Fails()
    {
        var module = Module(isAchieved: true);
        _membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, HeadId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var result = await Build().ApplyUnachieveAsync(TenantId, module);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("The current head must be an active employee in this tenant.");
        module.IsAchieved.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAllocationExtend_AboveParentSlack_Conflict()
    {
        var module = Module();
        _slack.Setup(x => x.CalculateAsync(TenantId, _parent, null, It.IsAny<CancellationToken>())).ReturnsAsync(3m);

        var result = await Build().ApplyAllocationExtendAsync(TenantId, module, new ModuleAllocationExtendInput(5m, "more"));

        result.StatusCode.Should().Be(409);
        module.AllocatedHours.Should().Be(10m);
    }

    [Fact]
    public async Task ApplyAllocationExtend_AddsHours()
    {
        var module = Module();
        _slack.Setup(x => x.CalculateAsync(TenantId, _parent, null, It.IsAny<CancellationToken>())).ReturnsAsync(30m);

        var result = await Build().ApplyAllocationExtendAsync(TenantId, module, new ModuleAllocationExtendInput(5m, "more"));

        result.IsSuccess.Should().BeTrue();
        module.AllocatedHours.Should().Be(15m);
    }
}
