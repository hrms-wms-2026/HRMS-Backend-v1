using FluentAssertions;
using ONEVO.Application.Features.WorkManagement.Approvals.Mappers;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

/// <summary>The approvals list / decide / cancel rows (WorkApprovalRequestResponse).</summary>
public class WorkApprovalRequestMapperTests
{
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private static WorkApprovalRequest Request(string actionType, string payloadJson) => new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), ActionType = actionType,
        TargetType = WorkTargetTypes.Meeting, TargetTitle = "Design review", ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = Approver, RequestedByEmployeeId = Requester, Status = WorkApprovalRequestStatuses.Pending,
        PayloadJson = payloadJson, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData(WorkActionTypes.MeetingCreate)]
    [InlineData(WorkActionTypes.MeetingMove)]
    public void A_meeting_row_never_carries_its_raw_payload(string actionType)
    {
        // The form holds more than D5 allows (location, privacy, participant ids); the snapshot travels only in the
        // approval detail's extraJson, to receivers.
        var row = Request(actionType, """{"form":{"location":"Room 4","participantEmployeeIds":["e1"]},"snapshot":{"title":"T"}}""");

        foreach (var viewer in new[] { Approver, Requester })
            WorkApprovalRequestMapper.ToResponse(row, new Dictionary<Guid, string>(), viewer).PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void Other_rows_keep_their_payload()
    {
        var row = Request(WorkActionTypes.TaskEdit, """{"title":"New"}""");

        WorkApprovalRequestMapper.ToResponse(row, new Dictionary<Guid, string>(), Approver).PayloadJson.Should().Be("""{"title":"New"}""");
    }
}
