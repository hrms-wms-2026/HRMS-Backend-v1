using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using ONEVO.Api.Controllers.Tenant.Monitoring.ActivityMonitoring;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Commands.IngestActivitySnapshots;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Helpers;
using ONEVO.Domain.Errors;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.Monitoring;

/// <summary>
/// AGT-01 wire contract the tray agent parses (ONEVO.Agent.Service/Api/ActivityIngestModels.cs
/// SnapshotIngestResponse, ActivitySyncService.ReadProblemAsync). Serialized with ASP.NET's web
/// defaults, as the API does.
/// </summary>
public class SnapshotIngestContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static MonitoringActivityIngestController Controller(Result<SnapshotIngestOutcome> result)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<IngestActivitySnapshotsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return new MonitoringActivityIngestController(mediator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new PlainProblemDetailsFactory()
        };
    }

    private static IngestActivitySnapshotsRequest Request() => new([
        new ActivitySnapshotRequestItem(DateTimeOffset.UtcNow, 1, 1, 30, 0, 5, "code.exe")
    ]);

    [Fact]
    public async Task Accepted_body_uses_the_field_names_the_agent_reads()
    {
        var rejectedAt = new DateTimeOffset(2026, 10, 11, 1, 2, 3, TimeSpan.Zero).AddTicks(1230);
        var outcome = new SnapshotIngestOutcome(2, 1, [new SnapshotRejection(rejectedAt, MonitoringErrors.SnapshotTooOldCode)]);

        var result = await Controller(Result<SnapshotIngestOutcome>.Success(outcome)).IngestSnapshots(Request(), default);

        var accepted = result.Should().BeOfType<AcceptedResult>().Which;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(accepted.Value, Web));
        json.RootElement.GetProperty("stored").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("duplicates").GetInt32().Should().Be(1);
        var rejected = json.RootElement.GetProperty("rejected")[0];
        rejected.GetProperty("captured_at").GetDateTimeOffset().Should().Be(rejectedAt);
        rejected.GetProperty("code").GetString().Should().Be("monitoring.snapshot_too_old");
    }

    [Fact]
    public async Task Failure_problem_details_carry_a_machine_readable_code()
    {
        var result = await Controller(Result<SnapshotIngestOutcome>.Failure(
                MonitoringErrors.SnapshotTooOld, 400, MonitoringErrors.SnapshotTooOldCode))
            .IngestSnapshots(Request(), default);

        var obj = result.Should().BeOfType<ObjectResult>().Which;
        obj.StatusCode.Should().Be(400);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(obj.Value, Web));
        json.RootElement.GetProperty("code").GetString().Should().Be("monitoring.snapshot_too_old");
        json.RootElement.GetProperty("detail").GetString().Should().Be(MonitoringErrors.SnapshotTooOld);
    }

    private sealed class PlainProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null,
            string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode };
    }
}
