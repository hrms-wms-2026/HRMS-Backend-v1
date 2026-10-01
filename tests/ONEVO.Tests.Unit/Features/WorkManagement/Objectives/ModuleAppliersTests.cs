using FluentAssertions;
using Moq;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Appliers;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;
using K = ONEVO.Tests.Unit.Features.WorkManagement.Objectives.ModuleHandlerTestKit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Objectives;

public class ModuleAppliersTests
{
    private readonly K _kit;

    public ModuleAppliersTests()
    {
        var module = K.NewModule();
        module.UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2);
        _kit = new K(module);
    }

    private static WorkApprovalRequest Request(string actionType, string payload, DateTimeOffset? snapshot = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = K.TenantId, ProjectId = K.ProjectId, ActionType = actionType, TargetType = WorkTargetTypes.Module,
        TargetId = K.ModuleId, RequestedByEmployeeId = K.Head, PayloadJson = payload, TargetUpdatedAtSnapshot = snapshot
    };

    private static Task<ApplyOutcome> Apply(IApprovalActionApplier applier, WorkApprovalRequest request, string? editedPayload = null)
        => applier.ApplyAsync(new ApprovalApplyContext(request, editedPayload ?? request.PayloadJson, K.ParentOwner), CancellationToken.None);

    private ModuleEditApplier Edit() => new(_kit.Objectives.Object, _kit.Writes());

    [Fact]
    public async Task Edit_UsesApproverEditedPayload()
    {
        var request = Request(WorkActionTypes.ModuleEdit,
            "{\"title\":\"Asked\",\"startDate\":\"2026-02-01\",\"endDate\":\"2026-04-01\",\"allocatedHours\":40}");

        var outcome = await Apply(Edit(), request,
            "{\"title\":\"Approved\",\"startDate\":\"2026-02-01\",\"endDate\":\"2026-03-15\",\"allocatedHours\":25}");

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        _kit.Module.Title.Should().Be("Approved");
        _kit.Module.AllocatedHours.Should().Be(25m);
        _kit.Module.EndDate.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public async Task Edit_ModuleChangedAfterRequest_Stale()
    {
        var request = Request(WorkActionTypes.ModuleEdit,
            "{\"title\":\"Asked\",\"startDate\":\"2026-02-01\",\"endDate\":\"2026-04-01\",\"allocatedHours\":40}",
            snapshot: DateTimeOffset.UtcNow.AddDays(-5));

        var outcome = await Apply(Edit(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
        _kit.Module.Title.Should().Be("Module");
    }

    [Fact]
    public async Task Edit_ReadsLegacyPascalCasePayload()
    {
        var request = Request(WorkActionTypes.ModuleEdit,
            "{\"Title\":\"Legacy\",\"Description\":null,\"StartDate\":\"2026-02-01\",\"EndDate\":\"2026-04-01\",\"AllocatedHours\":30}");

        var outcome = await Apply(Edit(), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        _kit.Module.Title.Should().Be("Legacy");
        _kit.Module.AllocatedHours.Should().Be(30m);
    }

    [Fact]
    public async Task Delete_AlreadyInactive_Stale()
    {
        _kit.Module.IsActive = false;

        var outcome = await Apply(new ModuleDeleteApplier(_kit.Objectives.Object, _kit.Writes()), Request(WorkActionTypes.ModuleDelete, "{}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
    }

    [Fact]
    public async Task Transfer_ReadsLegacyPayload_NewHeadEmployeeId()
    {
        var request = Request(WorkActionTypes.ModuleTransfer, "{\"NewHeadEmployeeId\":\"" + K.NewHead + "\"}");

        var outcome = await Apply(new ModuleTransferApplier(_kit.Objectives.Object, _kit.Writes()), request);

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        _kit.Module.OwnerId.Should().Be(K.NewHead);
    }

    [Fact]
    public async Task Achieve_AlreadyAchieved_Stale()
    {
        _kit.Module.IsAchieved = true;

        var outcome = await Apply(new ModuleAchieveApplier(_kit.Objectives.Object, _kit.Writes()), Request(WorkActionTypes.ModuleAchieve, "{}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Stale);
    }

    [Fact]
    public async Task AllocationExtend_ApproverLowersHours_AppliesLowerAmount()
    {
        // Snapshot is older than UpdatedAt on purpose: allocation extends never go stale on it.
        var request = Request(WorkActionTypes.ModuleAllocationExtend, "{\"requestedAdditionalHours\":20,\"reason\":\"scope\"}",
            snapshot: DateTimeOffset.UtcNow.AddDays(-5));

        var outcome = await Apply(new ModuleAllocationExtendApplier(_kit.Objectives.Object, _kit.Writes()), request,
            "{\"requestedAdditionalHours\":5,\"reason\":\"scope\"}");

        outcome.Kind.Should().Be(ApplyOutcomeKind.Applied);
        _kit.Module.AllocatedHours.Should().Be(15m);
    }

    [Fact]
    public async Task AllocationExtend_OverParentSlack_Invalid_StaysPending()
    {
        _kit.Slack.Setup(x => x.CalculateAsync(K.TenantId, _kit.Parent, null, It.IsAny<CancellationToken>())).ReturnsAsync(2m);

        var outcome = await Apply(new ModuleAllocationExtendApplier(_kit.Objectives.Object, _kit.Writes()),
            Request(WorkActionTypes.ModuleAllocationExtend, "{\"RequestedAdditionalHours\":5,\"Reason\":\"legacy\"}"));

        outcome.Kind.Should().Be(ApplyOutcomeKind.Invalid);
        outcome.Error.Should().StartWith("You don't have enough allocation yourself to approve this.");
        _kit.Module.AllocatedHours.Should().Be(10m);
    }
}
