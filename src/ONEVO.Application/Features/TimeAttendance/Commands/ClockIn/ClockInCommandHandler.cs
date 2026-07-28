using System.Net;
using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Context;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;

public sealed class ClockInCommandHandler
    : IRequestHandler<ClockInCommand, Result<ClockInResponse>>
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan VerificationFreshness = TimeSpan.FromMinutes(2);

    private readonly IClockInContextResolver _contexts;
    private readonly IAgentGatewayRepository _agents;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IVerificationRepository _verification;
    private readonly ILocationVerificationService _locations;
    private readonly IRequestNetworkContext _network;
    private readonly INetworkEvidenceHasher _hasher;
    private readonly IIdempotencyStore _idempotency;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ClockInCommandHandler(
        IClockInContextResolver contexts,
        IAgentGatewayRepository agents,
        ITimeAttendanceRepository attendance,
        IVerificationRepository verification,
        ILocationVerificationService locations,
        IRequestNetworkContext network,
        INetworkEvidenceHasher hasher,
        IIdempotencyStore idempotency,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _contexts = contexts;
        _agents = agents;
        _attendance = attendance;
        _verification = verification;
        _locations = locations;
        _network = network;
        _hasher = hasher;
        _idempotency = idempotency;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<ClockInResponse>> Handle(
        ClockInCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length is < 8 or > 128)
        {
            return Result<ClockInResponse>.Failure(
                "Idempotency-Key must contain 8 to 128 characters.",
                400);
        }

        var contextResult = await _contexts.ResolveAsync(
            request.AgentId,
            cancellationToken);
        if (!contextResult.IsSuccess)
        {
            return Result<ClockInResponse>.Failure(
                contextResult.Error ?? "Clock-in context is unavailable.",
                contextResult.StatusCode ?? 400);
        }

        var context = contextResult.Value!;
        if (context.AgentId != request.AgentId)
        {
            return Result<ClockInResponse>.Forbidden(
                "Clock-in context does not belong to the calling agent.");
        }

        var begin = await _idempotency.TryBeginAsync(
            request.IdempotencyKey,
            "time_attendance.clock_in",
            $"agent:{request.AgentId}",
            ClockActionRequestHasher.Hash(request),
            IdempotencyTtl,
            cancellationToken);
        var beginResult = ResolveBeginOutcome(begin);
        if (beginResult is not null)
            return beginResult;

        try
        {
            if (!context.CanClockIn)
            {
                var blocked = context.IsClockedIn
                    ? new ClockInResponse(
                        "already_clocked_in",
                        null,
                        null,
                        null,
                        null,
                        context.ReasonCode,
                        null,
                        200)
                    : new ClockInResponse(
                        "blocked_setup_required",
                        null,
                        null,
                        null,
                        null,
                        context.ReasonCode,
                        null,
                        409);
                return await CompleteAsync(
                    begin.RecordId,
                    blocked,
                    cancellationToken);
            }

            var now = _clock.UtcNow;
            var faceGate = await ValidateFaceAsync(
                context,
                request.VerificationRecordId,
                now,
                cancellationToken);
            if (faceGate is not null)
            {
                return await CompleteAsync(
                    begin.RecordId,
                    faceGate,
                    cancellationToken);
            }

            var locationResult = await EvaluateLocationAsync(
                context,
                request,
                now,
                cancellationToken);
            if (locationResult.BlockedResponse is not null)
            {
                await _uow.SaveChangesAsync(cancellationToken);
                return await CompleteAsync(
                    begin.RecordId,
                    locationResult.BlockedResponse,
                    cancellationToken);
            }

            var existingAttendance = await _attendance.GetAttendanceAsync(
                context.EmployeeId,
                context.WorkDate,
                cancellationToken);
            if (existingAttendance?.ActualStart is not null)
            {
                return await CompleteAsync(
                    begin.RecordId,
                    new ClockInResponse(
                        "already_clocked_in",
                        existingAttendance.Id,
                        null,
                        null,
                        null,
                        "attendance_already_started",
                        existingAttendance.ActualStart,
                        200),
                    cancellationToken);
            }

            var attendance = existingAttendance ?? CreateAttendance(
                context,
                locationResult.DetectedWorkArea,
                now);
            if (existingAttendance is null)
            {
                await _attendance.AddAttendanceAsync(
                    attendance,
                    cancellationToken);
            }
            else
            {
                StartExistingAttendance(
                    existingAttendance,
                    context,
                    locationResult.DetectedWorkArea,
                    now);
            }

            var presence = await _attendance.GetPresenceAsync(
                context.EmployeeId,
                context.WorkDate,
                cancellationToken);
            if (presence is null)
            {
                presence = new PresenceSession
                {
                    Id = Guid.NewGuid(),
                    TenantId = context.TenantId,
                    EmployeeId = context.EmployeeId,
                    Date = context.WorkDate,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    Source = "agent",
                    Status = "present",
                    CreatedAt = now
                };
                await _attendance.AddPresenceAsync(
                    presence,
                    cancellationToken);
            }
            else
            {
                presence.LastSeenAt = now;
                presence.Status = "present";
                presence.UpdatedAt = now;
            }

            await _attendance.AddDeviceSessionAsync(new DeviceSession
            {
                Id = Guid.NewGuid(),
                TenantId = context.TenantId,
                EmployeeId = context.EmployeeId,
                DeviceId = context.AgentId,
                SessionStart = now
            }, cancellationToken);

            if (locationResult.Evidence is not null)
            {
                locationResult.Evidence.PresenceSessionId = presence.Id;
                await _agents.AddWorkLocationEvidenceAsync(
                    locationResult.Evidence,
                    cancellationToken);
            }

            await _outbox.EnqueueAsync(
                OutboxMessageTypes.PresenceSessionStarted,
                new PresenceSessionStartedEvent(
                    context.TenantId,
                    context.AgentId,
                    context.EmployeeId,
                    attendance.Id,
                    presence.Id,
                    now,
                    context.MonitoringHardStopAt),
                context.TenantId,
                cancellationToken);

            await _uow.SaveChangesAsync(cancellationToken);

            return await CompleteAsync(
                begin.RecordId,
                new ClockInResponse(
                    "clocked_in",
                    attendance.Id,
                    presence.Id,
                    null,
                    null,
                    "ready",
                    now,
                    200),
                cancellationToken);
        }
        catch
        {
            await _idempotency.AbandonAsync(
                begin.RecordId,
                cancellationToken);
            throw;
        }
    }

    private Result<ClockInResponse>? ResolveBeginOutcome(
        IdempotencyBeginResult begin)
    {
        if (begin.Outcome == IdempotencyOutcome.Started)
            return null;

        if (begin.Outcome == IdempotencyOutcome.HashMismatch)
        {
            return Result<ClockInResponse>.Conflict(
                "Idempotency-Key was already used with a different request.");
        }

        if (begin.Outcome == IdempotencyOutcome.InFlight)
        {
            return Result<ClockInResponse>.Conflict(
                "A clock-in request with this Idempotency-Key is still in progress.");
        }

        if (string.IsNullOrWhiteSpace(begin.ResponseBody))
        {
            return Result<ClockInResponse>.Conflict(
                "Stored clock-in response is unavailable.");
        }

        try
        {
            var response = JsonSerializer.Deserialize<ClockInResponse>(
                begin.ResponseBody);
            return response is null
                ? Result<ClockInResponse>.Conflict(
                    "Stored clock-in response is invalid.")
                : Result<ClockInResponse>.Success(
                    response with
                    {
                        HttpStatusCode = begin.ResponseStatusCode ??
                            response.HttpStatusCode
                    });
        }
        catch (JsonException)
        {
            return Result<ClockInResponse>.Conflict(
                "Stored clock-in response is invalid.");
        }
    }

    private async Task<ClockInResponse?> ValidateFaceAsync(
        ResolvedClockInContext context,
        Guid? verificationRecordId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!context.PhotoRequired)
            return null;

        if (verificationRecordId is null)
        {
            return new ClockInResponse(
                "blocked_setup_required",
                null,
                null,
                null,
                null,
                "fresh_face_verification_required",
                null,
                409);
        }

        var record = await _verification.GetVerificationRecordAsync(
            verificationRecordId.Value,
            ct);
        var valid =
            record is not null &&
            record.TenantId == context.TenantId &&
            record.EmployeeId == context.EmployeeId &&
            record.AgentId == context.AgentId &&
            string.Equals(record.Status, "verified", StringComparison.Ordinal) &&
            string.Equals(record.Trigger, "clock_in", StringComparison.Ordinal) &&
            record.VerifiedAt <= now &&
            now - record.VerifiedAt <= VerificationFreshness;
        return valid
            ? null
            : new ClockInResponse(
                "blocked_verification_failed",
                null,
                null,
                null,
                null,
                "face_verification_invalid_or_expired",
                null,
                409);
    }

    private async Task<LocationEvaluation> EvaluateLocationAsync(
        ResolvedClockInContext context,
        ClockInCommand request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!context.LocationRequired)
        {
            return new LocationEvaluation(
                Evidence: null,
                DetectedWorkArea: DefaultDetectedArea(context.ExpectedWorkArea),
                BlockedResponse: null);
        }

        if (request.Capture is null)
        {
            return new LocationEvaluation(
                Evidence: null,
                DetectedWorkArea: null,
                BlockedResponse: new ClockInResponse(
                    "blocked_setup_required",
                    null,
                    null,
                    null,
                    null,
                    "location_capture_required",
                    null,
                    409));
        }

        var publicIp = _network.ClientIp;
        if (publicIp is null)
        {
            return new LocationEvaluation(
                Evidence: null,
                DetectedWorkArea: null,
                BlockedResponse: new ClockInResponse(
                    "blocked_setup_required",
                    null,
                    null,
                    null,
                    null,
                    "request_network_context_unavailable",
                    null,
                    409));
        }

        string? protectedWifi;
        string? protectedGateway;
        try
        {
            protectedWifi = _hasher.Protect(
                context.TenantId,
                request.WifiBssidHash);
            protectedGateway = _hasher.Protect(
                context.TenantId,
                request.GatewayMacHash);
        }
        catch (ArgumentException)
        {
            return new LocationEvaluation(
                Evidence: null,
                DetectedWorkArea: null,
                BlockedResponse: new ClockInResponse(
                    "blocked_setup_required",
                    null,
                    null,
                    null,
                    null,
                    "invalid_network_evidence_hash",
                    null,
                    409));
        }

        var validation = _locations.ValidateCapture(request.Capture, now);
        if (!validation.IsValid)
        {
            var invalidEvidence = CreateEvidence(
                context,
                request,
                publicIp,
                protectedWifi,
                protectedGateway,
                now,
                "unknown",
                null);
            await _agents.AddWorkLocationEvidenceAsync(
                invalidEvidence,
                ct);
            return new LocationEvaluation(
                invalidEvidence,
                null,
                new ClockInResponse(
                    "blocked_setup_required",
                    null,
                    null,
                    null,
                    null,
                    $"location_capture_invalid:{validation.FailureCode}",
                    null,
                    409));
        }

        if (context.ExpectedWorkArea == "field")
        {
            var fieldEvidence = CreateEvidence(
                context,
                request,
                publicIp,
                protectedWifi,
                protectedGateway,
                now,
                "not_evaluated",
                null);
            return new LocationEvaluation(
                fieldEvidence,
                "field",
                null);
        }

        var evaluations = context.LocationTargets
            .Select(target => new TargetEvaluation(
                target,
                _locations.Evaluate(request.Capture, target, now)))
            .Where(evaluation => evaluation.Result.IsValid)
            .ToArray();
        var expectedMatch = evaluations.FirstOrDefault(evaluation =>
            evaluation.Result.IsMatch &&
            IsExpectedTarget(context.ExpectedWorkArea, evaluation.Target));
        var anyMatch = evaluations.FirstOrDefault(
            evaluation => evaluation.Result.IsMatch);

        if (expectedMatch is not null)
        {
            var evidence = CreateEvidence(
                context,
                request,
                publicIp,
                protectedWifi,
                protectedGateway,
                now,
                "matched",
                expectedMatch.Target);
            return new LocationEvaluation(
                evidence,
                ToDetectedArea(expectedMatch.Target),
                null);
        }

        var mismatchEvidence = CreateEvidence(
            context,
            request,
            publicIp,
            protectedWifi,
            protectedGateway,
            now,
            "mismatch",
            anyMatch?.Target);
        await _agents.AddWorkLocationEvidenceAsync(
            mismatchEvidence,
            ct);

        var approval = await CreateApprovalAsync(
            context,
            request,
            publicIp,
            protectedWifi,
            protectedGateway,
            anyMatch?.Target,
            now,
            ct);
        return new LocationEvaluation(
            mismatchEvidence,
            anyMatch is null ? null : ToDetectedArea(anyMatch.Target),
            new ClockInResponse(
                "blocked_pending_approval",
                null,
                null,
                approval.RequestId,
                approval.RequestType,
                "location_mismatch_requires_hr_approval",
                null,
                409));
    }

    private async Task<ApprovalReference> CreateApprovalAsync(
        ResolvedClockInContext context,
        ClockInCommand request,
        IPAddress publicIp,
        string? protectedWifi,
        string? protectedGateway,
        LocationTarget? matchedAlternative,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (context.ExpectedWorkArea is "remote" or "either")
        {
            var existingRemote =
                await _verification.GetPendingRemoteChangeAsync(
                    context.EmployeeId,
                    ct);
            if (existingRemote is not null)
            {
                return new ApprovalReference(
                    existingRemote.Id,
                    "remote_location_change");
            }

            var activeProfile =
                await _verification.GetActiveRemoteProfileAsync(
                    context.EmployeeId,
                    ct);
            if (activeProfile is not null)
            {
                var candidate = new EmployeeRemoteWorkProfile
                {
                    Id = Guid.NewGuid(),
                    TenantId = context.TenantId,
                    EmployeeId = context.EmployeeId,
                    Status = "pending_capture",
                    CapturedAt = now,
                    PublicIp = publicIp,
                    WifiBssidHash = protectedWifi,
                    GatewayMacHash = protectedGateway,
                    VpnDetected = request.VpnDetected,
                    CoarseLocationJson = SerializeCapture(
                        request.Capture!,
                        request.LocalNetworkClass),
                    CreatedAt = now
                };
                var remoteRequest = new RemoteWorkLocationChangeRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = context.TenantId,
                    EmployeeId = context.EmployeeId,
                    CurrentProfileId = activeProfile.Id,
                    NewProfileId = candidate.Id,
                    Reason =
                        "Clock-in was attempted from a different remote location.",
                    Status = "pending",
                    RequestedAt = now
                };
                await _verification.AddRemoteProfileAsync(candidate, ct);
                await _verification.AddRemoteChangeRequestAsync(
                    remoteRequest,
                    ct);
                return new ApprovalReference(
                    remoteRequest.Id,
                    "remote_location_change");
            }
        }

        var existingWorkArea =
            await _attendance.GetPendingWorkAreaChangeAsync(
                context.EmployeeId,
                context.WorkDate,
                ct);
        if (existingWorkArea is not null)
        {
            return new ApprovalReference(
                existingWorkArea.Id,
                "work_area_change");
        }

        var detectedArea = matchedAlternative is null
            ? context.ExpectedWorkArea switch
            {
                "onsite" => "remote",
                "remote" => "onsite",
                "either" => "field",
                _ => "field"
            }
            : ToDetectedArea(matchedAlternative);
        if (detectedArea == context.ExpectedWorkArea)
            detectedArea = context.ExpectedWorkArea == "onsite"
                ? "remote"
                : "onsite";

        var workAreaRequest = new WorkAreaChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            EmployeeId = context.EmployeeId,
            LegalEntityId = context.LegalEntityId,
            Date = context.WorkDate,
            CurrentExpectedWorkArea = context.ExpectedWorkArea,
            RequestedWorkArea = detectedArea,
            Reason =
                "Clock-in location did not match the scheduled work area.",
            Status = "pending",
            RequestedAt = now
        };
        await _attendance.AddWorkAreaChangeAsync(
            workAreaRequest,
            ct);
        return new ApprovalReference(
            workAreaRequest.Id,
            "work_area_change");
    }

    private async Task<Result<ClockInResponse>> CompleteAsync(
        Guid idempotencyRecordId,
        ClockInResponse response,
        CancellationToken ct)
    {
        var responseJson = JsonSerializer.Serialize(response);
        await _idempotency.CompleteAsync(
            idempotencyRecordId,
            response.HttpStatusCode,
            responseJson,
            ct);
        return Result<ClockInResponse>.Success(response);
    }

    private static AttendanceRecord CreateAttendance(
        ResolvedClockInContext context,
        string? detectedWorkArea,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = context.TenantId,
        EmployeeId = context.EmployeeId,
        Date = context.WorkDate,
        WorkScheduleId = context.WorkScheduleId,
        ExpectedWorkingDay = context.IsWorkingDay,
        WorkTimeType =
            context.ScheduledStart is null || context.ScheduledEnd is null
                ? "flexible"
                : "fixed",
        ScheduledStart = context.ScheduledStart,
        ScheduledEnd = context.ScheduledEnd,
        RequiredWorkMinutes = context.RequiredWorkMinutes,
        ExpectedWorkArea = context.ExpectedWorkArea,
        ScheduleTimezone = context.Timezone,
        ActualStart = now,
        DetectedWorkArea = detectedWorkArea,
        AttendanceSource = "agent",
        Status = "on_time",
        CreatedAt = now
    };

    private static void StartExistingAttendance(
        AttendanceRecord attendance,
        ResolvedClockInContext context,
        string? detectedWorkArea,
        DateTimeOffset now)
    {
        attendance.WorkScheduleId = context.WorkScheduleId;
        attendance.ExpectedWorkingDay = context.IsWorkingDay;
        attendance.ScheduledStart = context.ScheduledStart;
        attendance.ScheduledEnd = context.ScheduledEnd;
        attendance.RequiredWorkMinutes = context.RequiredWorkMinutes;
        attendance.ExpectedWorkArea = context.ExpectedWorkArea;
        attendance.ScheduleTimezone = context.Timezone;
        attendance.ActualStart = now;
        attendance.DetectedWorkArea = detectedWorkArea;
        attendance.AttendanceSource = "agent";
        attendance.Status = "on_time";
        attendance.UpdatedAt = now;
    }

    private static AgentWorkLocationEvidence CreateEvidence(
        ResolvedClockInContext context,
        ClockInCommand request,
        IPAddress publicIp,
        string? protectedWifi,
        string? protectedGateway,
        DateTimeOffset now,
        string matchStatus,
        LocationTarget? target) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = context.TenantId,
        AgentId = context.AgentId,
        EmployeeId = context.EmployeeId,
        CapturedAt = request.Capture!.CapturedAt,
        ReceivedAt = now,
        PublicIp = publicIp,
        WifiBssidHash = protectedWifi,
        GatewayMacHash = protectedGateway,
        VpnDetected = request.VpnDetected,
        CoarseLocationJson = SerializeCapture(
            request.Capture,
            request.LocalNetworkClass),
        MatchStatus = matchStatus,
        Confidence = request.Capture.AccuracyMeters <= 50m
            ? "high"
            : "medium",
        MatchedLocationSource = target?.Source,
        MatchedLocationSourceId = target?.SourceId,
        CreatedAt = now
    };

    private static string SerializeCapture(
        LocationCapture capture,
        string? localNetworkClass) =>
        JsonSerializer.Serialize(new
        {
            capture.Latitude,
            capture.Longitude,
            capture.AccuracyMeters,
            capture.CapturedAt,
            capture.PermissionState,
            LocalNetworkClass = localNetworkClass
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

    private static bool IsExpectedTarget(
        string expectedWorkArea,
        LocationTarget target) =>
        expectedWorkArea switch
        {
            "onsite" => target.Source == "company_office",
            "remote" => target.Source == "remote_profile",
            "either" => target.Source is "company_office" or "remote_profile",
            _ => false
        };

    private static string ToDetectedArea(LocationTarget target) =>
        target.Source == "company_office"
            ? "onsite"
            : "remote";

    private static string? DefaultDetectedArea(string expectedWorkArea) =>
        expectedWorkArea is "onsite" or "remote" or "field"
            ? expectedWorkArea
            : null;

    private sealed record TargetEvaluation(
        LocationTarget Target,
        LocationMatchResult Result);

    private sealed record LocationEvaluation(
        AgentWorkLocationEvidence? Evidence,
        string? DetectedWorkArea,
        ClockInResponse? BlockedResponse);

    private sealed record ApprovalReference(
        Guid RequestId,
        string RequestType);
}
