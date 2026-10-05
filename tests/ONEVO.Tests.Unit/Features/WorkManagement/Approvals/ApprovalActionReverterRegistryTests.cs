using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalActionReverterRegistryTests
{
    private sealed class FakeReverter : IApprovalActionReverter
    {
        public string ActionType { get; init; } = WorkActionTypes.ModuleEdit;
        public Task<RevertOutcome> RevertAsync(ApprovalRevertContext context, CancellationToken ct)
            => Task.FromResult(RevertOutcome.Reverted());
    }

    [Fact]
    public void Find_ReturnsRegisteredReverter_ByActionType()
    {
        var registry = new ApprovalActionReverterRegistry(new IApprovalActionReverter[] { new FakeReverter() });
        registry.Find(WorkActionTypes.ModuleEdit).Should().NotBeNull();
        registry.Find(WorkActionTypes.TaskDelete).Should().BeNull();
    }

    [Fact]
    public void Constructor_TwoRevertersForSameActionType_Throws()
    {
        var act = () => new ApprovalActionReverterRegistry(new IApprovalActionReverter[]
        {
            new FakeReverter(), new FakeReverter()
        });
        act.Should().Throw<InvalidOperationException>();
    }
}
