using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Reverters;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Sprints.Reverters;

public class SprintEditReverterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly Mock<ISprintWriteService> _writes = new();
    private readonly Mock<ISprintRepository> _sprints = new();

    private SprintEditReverter Build() => new(_writes.Object, _sprints.Object);

    [Fact]
    public async Task RevertAsync_RestoresTheSnapshottedFields()
    {
        var sprintId = Guid.NewGuid();
        var sprint = new Sprint { Id = sprintId, TenantId = TenantId, Name = "New name" };
        var undo = new SprintEditInput("Old name", "Old goal", null, null);
        var request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TargetId = sprintId, ActionType = WorkActionTypes.SprintEdit,
            UndoStateJson = JsonSerializer.Serialize(undo, SprintPayloadJson.Options)
        };
        _sprints.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, sprintId, It.IsAny<CancellationToken>())).ReturnsAsync(sprint);
        _writes.Setup(x => x.ApplyEditAsync(TenantId, It.IsAny<Guid>(), sprint, undo, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, Sprint, SprintEditInput, CancellationToken>((_, _, s, i, _) => s.Name = i.Name)
            .ReturnsAsync(Result.Success());

        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);

        outcome.Kind.Should().Be(RevertOutcomeKind.Reverted);
        sprint.Name.Should().Be("Old name");
    }

    [Fact]
    public async Task RevertAsync_NoUndoSnapshot_ReturnsNotRevertable()
    {
        var request = new WorkApprovalRequest { Id = Guid.NewGuid(), TenantId = TenantId, TargetId = Guid.NewGuid(), ActionType = WorkActionTypes.SprintEdit, UndoStateJson = null };
        var outcome = await Build().RevertAsync(new ApprovalRevertContext(request, Guid.NewGuid()), CancellationToken.None);
        outcome.Kind.Should().Be(RevertOutcomeKind.NotRevertable);
    }
}
