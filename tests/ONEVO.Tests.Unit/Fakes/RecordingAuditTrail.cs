using System.Text.Json;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Tests.Unit.Fakes;

/// <summary>Captures the before-state a handler records, as the JSON the audit row would store.</summary>
public sealed class RecordingAuditTrail : IAuditTrail
{
    public object? Before { get; private set; }

    public string? TargetId { get; private set; }

    public void RecordBefore(object before) => Before = before;

    public void RecordTarget(string targetId) => TargetId = targetId;

    public JsonElement BeforeJson() =>
        JsonDocument.Parse(JsonSerializer.Serialize(Before, new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement;
}
