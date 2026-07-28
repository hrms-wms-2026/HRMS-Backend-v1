using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteScreenshotCommand;

public sealed class CompleteScreenshotCommandHandler
    : IRequestHandler<
        CompleteScreenshotCommand,
        Result<CompleteScreenshotResponse>>
{
    private const int DefaultMaxBytes = 2 * 1024 * 1024;

    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IUserProfileRepository _profiles;
    private readonly IActivityMonitoringRepository _monitoring;
    private readonly IFileStorageService _files;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public CompleteScreenshotCommandHandler(
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IUserProfileRepository profiles,
        IActivityMonitoringRepository monitoring,
        IFileStorageService files,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _attendance = attendance;
        _profiles = profiles;
        _monitoring = monitoring;
        _files = files;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<CompleteScreenshotResponse>> Handle(
        CompleteScreenshotCommand request,
        CancellationToken cancellationToken)
    {
        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<CompleteScreenshotResponse>.Forbidden(
                "Agent is not an approved active device.");
        }

        var command = await _agents.GetCommandByIdAsync(
            request.CommandId,
            cancellationToken);
        if (command is null ||
            command.AgentId != agent.Id ||
            command.TenantId != agent.TenantId ||
            command.EmployeeId != agent.EmployeeId.Value)
        {
            return Result<CompleteScreenshotResponse>.NotFound(
                "Agent command not found.");
        }
        if (!string.Equals(
                command.CommandType,
                "screenshot_capture_request",
                StringComparison.Ordinal) ||
            !string.Equals(command.Status, "accepted", StringComparison.Ordinal) ||
            !string.Equals(command.Decision, "allow", StringComparison.Ordinal))
        {
            return Result<CompleteScreenshotResponse>.Conflict(
                "Fresh employee screenshot consent is required.");
        }

        var now = _clock.UtcNow;
        if (command.ExpiresAt <= now)
        {
            command.Status = "expired";
            command.ResultCode = "capture_timeout";
            command.CompletedAt = now;
            await _uow.SaveChangesAsync(cancellationToken);
            return Result<CompleteScreenshotResponse>.Conflict(
                "Screenshot capture window expired.");
        }

        var deviceSession = await _attendance.GetOpenDeviceSessionAsync(
            agent.Id,
            cancellationToken);
        if (deviceSession is null ||
            deviceSession.TenantId != agent.TenantId ||
            deviceSession.EmployeeId != agent.EmployeeId.Value ||
            deviceSession.DeviceId != agent.Id ||
            deviceSession.SessionEnd is not null)
        {
            return Result<CompleteScreenshotResponse>.Conflict(
                "Screenshot capture is valid only during active work.");
        }

        if (!request.Content.CanSeek)
        {
            return Result<CompleteScreenshotResponse>.Failure(
                "Screenshot stream must be buffered.",
                400);
        }

        var maxBytes = ReadMaxBytes(command.PayloadJson);
        if (request.Content.Length is <= 0 ||
            request.Content.Length > maxBytes)
        {
            return Result<CompleteScreenshotResponse>.Failure(
                "Screenshot size is outside the configured limit.",
                400);
        }
        if (!await HasValidImageSignatureAsync(
                request.Content,
                request.ContentType,
                cancellationToken))
        {
            return Result<CompleteScreenshotResponse>.Failure(
                "Screenshot content does not match its image type.",
                400);
        }

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
        {
            return Result<CompleteScreenshotResponse>.NotFound(
                "Employee not found.");
        }

        request.Content.Position = 0;
        var extension = string.Equals(
            request.ContentType,
            "image/png",
            StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : ".jpg";
        var upload = await _files.UploadAsync(
            agent.TenantId,
            employee.UserId,
            $"screenshot-{command.Id:N}{extension}",
            request.ContentType.ToLowerInvariant(),
            UploadPurposeCatalog.MonitoringScreenshot,
            request.Content,
            cancellationToken);
        if (!upload.IsSuccess)
        {
            return Result<CompleteScreenshotResponse>.Failure(
                upload.Error ?? "Screenshot storage failed.",
                upload.StatusCode ?? 500);
        }

        var evidence = new MonitoringEvidenceAsset
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = employee.Id,
            AgentDeviceId = agent.Id,
            ActivitySnapshotId = command.ActivitySnapshotId,
            CapturedAt = now,
            FileRecordId = upload.Value!.Id,
            EvidenceType = "screenshot",
            TriggerType = "auto_deviation",
            CreatedAt = now
        };
        await _monitoring.AddMonitoringEvidenceAsync(
            evidence,
            cancellationToken);
        command.Status = "completed";
        command.MonitoringEvidenceAssetId = evidence.Id;
        command.ResultCode = "captured_with_employee_consent";
        command.CompletedAt = now;
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<CompleteScreenshotResponse>.Success(
            new CompleteScreenshotResponse(
                command.Id,
                evidence.Id,
                now,
                command.Status));
    }

    private static int ReadMaxBytes(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty(
                    "max_screenshot_bytes",
                    out var value) &&
                value.TryGetInt32(out var parsed))
            {
                return Math.Clamp(parsed, 64 * 1024, 5 * 1024 * 1024);
            }
        }
        catch (JsonException)
        {
            // Fail closed to the smaller default.
        }

        return DefaultMaxBytes;
    }

    private static async Task<bool> HasValidImageSignatureAsync(
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        var header = new byte[8];
        content.Position = 0;
        var read = await content.ReadAsync(header.AsMemory(0, 8), ct);
        content.Position = 0;
        if (string.Equals(
                contentType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase))
        {
            return read >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF;
        }
        if (string.Equals(
                contentType,
                "image/png",
                StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<byte> png =
            [
                0x89, 0x50, 0x4E, 0x47,
                0x0D, 0x0A, 0x1A, 0x0A
            ];
            return read == 8 && header.AsSpan().SequenceEqual(png);
        }

        return false;
    }
}

