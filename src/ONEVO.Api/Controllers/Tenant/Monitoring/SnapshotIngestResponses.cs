using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;

namespace ONEVO.Api.Controllers.Tenant.Monitoring;

/// <summary>
/// Shared result mapping for tray snapshot ingest. 202 carries which items were stored, already
/// stored, or permanently rejected so the agent can acknowledge or quarantine each record; failures
/// carry a machine-readable <c>code</c> next to the human <c>detail</c>. Agents that predate this
/// body ignore it.
/// </summary>
internal static class SnapshotIngestResponses
{
    public static IActionResult ToActionResult(ControllerBase controller, Result<SnapshotIngestOutcome> result)
    {
        if (!result.IsSuccess)
        {
            var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
                controller.HttpContext, statusCode: result.StatusCode ?? 400, detail: result.Error);
            if (result.ErrorCode is not null)
                problem.Extensions["code"] = result.ErrorCode;
            return new ObjectResult(problem) { StatusCode = problem.Status };
        }

        var outcome = result.Value!;
        return controller.Accepted(new SnapshotIngestResponse(
            outcome.Stored,
            outcome.Duplicates,
            outcome.Rejected.Select(r => new SnapshotIngestRejection(r.CapturedAt, r.Code)).ToList()));
    }
}

public sealed record SnapshotIngestResponse(
    [property: JsonPropertyName("stored")] int Stored,
    [property: JsonPropertyName("duplicates")] int Duplicates,
    [property: JsonPropertyName("rejected")] IReadOnlyList<SnapshotIngestRejection> Rejected);

public sealed record SnapshotIngestRejection(
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("code")] string Code);
