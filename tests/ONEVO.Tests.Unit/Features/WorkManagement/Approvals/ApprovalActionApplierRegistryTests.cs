using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalActionApplierRegistryTests
{
    private sealed class StubApplier(string actionType) : IApprovalActionApplier
    {
        public string ActionType { get; } = actionType;
        public Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct) => Task.FromResult(ApplyOutcome.Applied());
    }

    [Fact]
    public void Find_ReturnsRegisteredApplier_NullOtherwise()
    {
        var edit = new StubApplier("task.edit");
        var registry = new ApprovalActionApplierRegistry(new[] { edit });
        registry.Find("task.edit").Should().BeSameAs(edit);
        registry.Find("task.delete").Should().BeNull();
    }

    [Fact]
    public void DuplicateActionType_FailsFastAtConstruction()
    {
        var act = () => new ApprovalActionApplierRegistry(new[] { new StubApplier("task.edit"), new StubApplier("task.edit") });
        act.Should().Throw<InvalidOperationException>().WithMessage("*task.edit*");
    }
}
