using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

public sealed record CalendarIncidentEvaluatePayload(Guid TenantId, IReadOnlyList<Guid> EmployeeIds);

/// <summary>
/// Re-runs the calendar meeting-exception rules for the employees a change touched (plan C2 triggers: an RSVP change, an
/// event created / changed / removed, a participant removed, leave approved, a meeting's attendance report settled).
/// Every employee is evaluated even when one fails; the failure is rethrown so the outbox retries - evaluation is
/// idempotent.
/// </summary>
public sealed class CalendarIncidentEvaluateOutboxHandler(CalendarIncidentEngine engine, ILogger<CalendarIncidentEvaluateOutboxHandler> logger)
    : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.CalendarIncidentEvaluate;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarIncidentEvaluatePayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_incident_evaluate payload is empty.");
        Exception? first = null;
        foreach (var employeeId in payload.EmployeeIds.Distinct())
        {
            try
            {
                await engine.EvaluateEmployeeAsync(payload.TenantId, employeeId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Calendar incident evaluation failed for employee {EmployeeId}.", employeeId);
                first ??= ex;
            }
        }
        if (first is not null)
            throw new InvalidOperationException("Calendar incident evaluation failed for at least one employee.", first);
    }
}
