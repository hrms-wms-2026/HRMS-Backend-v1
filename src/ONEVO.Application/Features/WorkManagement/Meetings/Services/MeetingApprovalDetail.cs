using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// What a meeting.create / meeting.move approval detail shows (D5, spec 5 "Approval detail | Snapshot only"): the
/// snapshot frozen at submit, as <c>{"snapshot":{...}}</c>. Never the raw payload, whose form carries more than D5 allows.
/// </summary>
public static class MeetingApprovalDetail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsMeetingAction(string actionType)
        => actionType is WorkActionTypes.MeetingCreate or WorkActionTypes.MeetingMove;

    /// <summary>The snapshot of a stored payload, or null when it has none (a move submitted before snapshots) or cannot be
    /// read. Re-serialized through <see cref="MeetingApprovalSnapshot"/>, so only its D5 fields can ever pass through.</summary>
    public static string? ExtraJson(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            if (payload.RootElement.ValueKind != JsonValueKind.Object
                || !payload.RootElement.TryGetProperty("snapshot", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return null;
            var snapshot = element.Deserialize<MeetingApprovalSnapshot>(Json);
            return snapshot is null ? null : JsonSerializer.Serialize(new { snapshot }, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
