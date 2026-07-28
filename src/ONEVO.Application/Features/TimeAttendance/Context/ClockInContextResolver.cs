using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.Location;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.Configuration.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Context;

public sealed class ClockInContextResolver : IClockInContextResolver
{
    private const int DefaultRemoteRadiusMeters = 250;

    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IVerificationRepository _verification;
    private readonly IDateTimeProvider _clock;

    public ClockInContextResolver(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        ILegalEntityRepository legalEntities,
        ITimeAttendanceRepository attendance,
        IVerificationRepository verification,
        IDateTimeProvider clock)
    {
        _agents = agents;
        _profiles = profiles;
        _legalEntities = legalEntities;
        _attendance = attendance;
        _verification = verification;
        _clock = clock;
    }

    public async Task<Result<ResolvedClockInContext>> ResolveAsync(
        Guid agentId,
        CancellationToken ct)
    {
        var agent = await _agents.GetAgentByIdAsync(agentId, ct);
        if (agent is null)
            return Result<ResolvedClockInContext>.NotFound("Agent not found.");

        if (!string.Equals(agent.Status, "active", StringComparison.Ordinal) ||
            agent.EmployeeId is null)
        {
            return Result<ResolvedClockInContext>.Forbidden(
                "Agent is not an approved active device.");
        }

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            ct);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<ResolvedClockInContext>.NotFound("Employee not found.");

        if (employee.LegalEntityId is null)
        {
            return Result<ResolvedClockInContext>.Conflict(
                "Employee does not have a Company assignment.");
        }

        var legalEntity = await _legalEntities.GetByIdAsync(
            employee.LegalEntityId.Value,
            ct);
        if (legalEntity is null ||
            legalEntity.TenantId != agent.TenantId ||
            !legalEntity.IsActive)
        {
            return Result<ResolvedClockInContext>.Conflict(
                "Employee Company assignment is not active.");
        }

        if (!TryResolveTimezone(legalEntity.Timezone, out var timezone))
        {
            return Result<ResolvedClockInContext>.Conflict(
                "Company timezone is not configured correctly.");
        }

        var localNow = TimeZoneInfo.ConvertTime(_clock.UtcNow, timezone);
        var workDate = DateOnly.FromDateTime(localNow.DateTime);
        var assignment = await _attendance.ResolveScheduleAssignmentAsync(
            employee,
            workDate,
            ct);
        if (assignment is null || assignment.TenantId != agent.TenantId)
        {
            return Result<ResolvedClockInContext>.Conflict(
                "Employee work schedule is not configured.");
        }

        var schedule = await _attendance.GetScheduleAsync(
            assignment.WorkScheduleId,
            ct);
        if (schedule is null ||
            schedule.TenantId != agent.TenantId ||
            schedule.LegalEntityId != legalEntity.Id)
        {
            return Result<ResolvedClockInContext>.Conflict(
                "Employee work schedule is not active.");
        }

        var dayOfWeek = ToIsoDayOfWeek(localNow.DayOfWeek);
        var scheduleDay = await _attendance.GetScheduleDayAsync(
            schedule.Id,
            dayOfWeek,
            ct);
        var holiday = await _attendance.GetScheduleHolidayAsync(
            schedule.Id,
            workDate,
            ct);
        var clockInPolicy = await _attendance.ResolveClockInPolicyAsync(
            employee,
            legalEntity.Id,
            workDate,
            ct);
        if (clockInPolicy is null ||
            clockInPolicy.TenantId != agent.TenantId ||
            !clockInPolicy.IsActive)
        {
            return Result<ResolvedClockInContext>.Conflict(
                "An active clock-in policy is not configured.");
        }

        var settings = await _profiles.GetWorkLocationSettingsAsync(
            employee.Id,
            ct);
        var approvedChange = await _attendance.GetApprovedWorkAreaChangeAsync(
            employee.Id,
            workDate,
            ct);
        var (expectedWorkArea, workAreaSource) = ResolveExpectedWorkArea(
            approvedChange,
            scheduleDay,
            settings);

        var verificationPolicy =
            await _verification.GetActivePolicyAsync(ct);
        var reference = await _verification.GetActiveReferencePhotoAsync(
            employee.Id,
            ct);
        var remoteProfile = await _verification.GetActiveRemoteProfileAsync(
            employee.Id,
            ct);
        var locationTargets = BuildLocationTargets(
            clockInPolicy,
            legalEntity,
            remoteProfile);
        var locationRequired = ResolveLocationRequired(
            expectedWorkArea,
            settings,
            clockInPolicy);
        var photoRequired = ResolvePhotoRequired(
            expectedWorkArea,
            clockInPolicy,
            verificationPolicy);
        var referenceReady =
            reference is not null &&
            reference.TenantId == agent.TenantId &&
            reference.EmployeeId == employee.Id &&
            reference.IsActive &&
            string.Equals(reference.Status, "approved", StringComparison.Ordinal);
        var remoteProfileReady =
            remoteProfile is not null &&
            remoteProfile.TenantId == agent.TenantId &&
            remoteProfile.EmployeeId == employee.Id &&
            string.Equals(remoteProfile.Status, "active", StringComparison.Ordinal) &&
            TryCreateRemoteTarget(remoteProfile, out _);

        var openDeviceSession = await _attendance.GetOpenDeviceSessionAsync(
            agent.Id,
            ct);
        var isClockedIn =
            openDeviceSession is not null &&
            openDeviceSession.SessionEnd is null &&
            openDeviceSession.TenantId == agent.TenantId &&
            openDeviceSession.EmployeeId == employee.Id &&
            openDeviceSession.DeviceId == agent.Id;

        var isWorkingDay =
            holiday is null &&
            scheduleDay is { IsWorkingDay: true };
        var (canClockIn, reasonCode) = ResolveEligibility(
            isClockedIn,
            holiday,
            scheduleDay,
            expectedWorkArea,
            clockInPolicy,
            locationRequired,
            locationTargets,
            remoteProfileReady,
            photoRequired,
            referenceReady,
            verificationPolicy);

        var hardStopAt = CalculateHardStop(
            workDate,
            scheduleDay,
            timezone);

        return Result<ResolvedClockInContext>.Success(
            new ResolvedClockInContext(
                TenantId: agent.TenantId,
                AgentId: agent.Id,
                EmployeeId: employee.Id,
                LegalEntityId: legalEntity.Id,
                WorkDate: workDate,
                Timezone: legalEntity.Timezone,
                WorkScheduleId: schedule.Id,
                WorkScheduleName: schedule.Name,
                ScheduledStart: scheduleDay?.StartTime,
                ScheduledEnd: scheduleDay?.EndTime,
                RequiredWorkMinutes: scheduleDay?.RequiredWorkMinutes,
                ExpectedWorkArea: expectedWorkArea,
                WorkAreaSource: workAreaSource,
                ClockInPolicyId: clockInPolicy.Id,
                LocationRequired: locationRequired,
                LocationTargets: locationTargets,
                PhotoRequired: photoRequired,
                ReferenceReady: referenceReady,
                RemoteProfileReady: remoteProfileReady,
                IsHoliday: holiday is not null,
                IsWorkingDay: isWorkingDay,
                IsClockedIn: isClockedIn,
                CanClockIn: canClockIn,
                ReasonCode: reasonCode,
                MonitoringHardStopAt: hardStopAt));
    }

    private static (
        string ExpectedWorkArea,
        string Source) ResolveExpectedWorkArea(
            WorkAreaChangeRequest? approvedChange,
            WorkScheduleDay? scheduleDay,
            EmployeeWorkLocationSettings? settings)
    {
        if (approvedChange is not null &&
            string.Equals(
                approvedChange.Status,
                "approved",
                StringComparison.Ordinal))
        {
            return (
                NormalizeWorkArea(approvedChange.RequestedWorkArea),
                "approved_work_area_change");
        }

        if (!string.IsNullOrWhiteSpace(scheduleDay?.ExpectedWorkArea))
        {
            return (
                NormalizeWorkArea(scheduleDay.ExpectedWorkArea),
                "work_schedule");
        }

        return (
            NormalizeWorkArea(settings?.WorkMode),
            "employee_work_mode");
    }

    private static bool ResolveLocationRequired(
        string workArea,
        EmployeeWorkLocationSettings? settings,
        ClockInPolicy policy)
    {
        if (settings is { WorkLocationVerificationEnabled: false })
            return false;

        return workArea == "either"
            ? policy.LocationVerificationRequired ||
              policy.EitherLocationCheckRequired
            : policy.LocationVerificationRequired;
    }

    private static bool ResolvePhotoRequired(
        string workArea,
        ClockInPolicy policy,
        VerificationPolicy? verificationPolicy)
    {
        var areaPolicyRequiresPhoto = workArea switch
        {
            "onsite" => policy.OnsitePhotoRequired,
            "remote" => policy.RemotePhotoRequired,
            "either" => policy.EitherPhotoRequired,
            "field" => string.Equals(
                policy.FieldPhotoRequirement,
                "required",
                StringComparison.Ordinal),
            _ => false
        };

        var verificationRequiresPhoto =
            verificationPolicy is { IsActive: true, RequirePhotoClockIn: true } &&
            AppliesToArea(
                verificationPolicy.PhotoCaptureContextScope,
                workArea);
        return areaPolicyRequiresPhoto || verificationRequiresPhoto;
    }

    private static (
        bool CanClockIn,
        string ReasonCode) ResolveEligibility(
            bool isClockedIn,
            WorkScheduleHoliday? holiday,
            WorkScheduleDay? scheduleDay,
            string workArea,
            ClockInPolicy clockInPolicy,
            bool locationRequired,
            IReadOnlyList<LocationTarget> locationTargets,
            bool remoteProfileReady,
            bool photoRequired,
            bool referenceReady,
            VerificationPolicy? verificationPolicy)
    {
        if (isClockedIn)
            return (false, "already_clocked_in");
        if (holiday is not null)
            return (false, "holiday");
        if (scheduleDay is null)
            return (false, "schedule_day_not_configured");
        if (!scheduleDay.IsWorkingDay)
            return (false, "off_day");
        if (!IsTrayEnabled(workArea, clockInPolicy))
            return (false, "tray_clock_in_disabled");
        if (locationRequired &&
            workArea == "remote" &&
            !remoteProfileReady)
        {
            return (false, "remote_location_setup_required");
        }
        if (locationRequired &&
            workArea is "onsite" or "remote" or "either" &&
            !HasExpectedLocationTarget(workArea, locationTargets))
        {
            return (false, "work_location_not_configured");
        }
        if (photoRequired &&
            verificationPolicy is not
            {
                IsActive: true,
                CameraPhotoVerificationEnabled: true
            })
        {
            return (false, "photo_verification_not_configured");
        }
        if (photoRequired && !referenceReady)
            return (false, "reference_photo_required");
        return (true, "ready");
    }

    private static IReadOnlyList<LocationTarget> BuildLocationTargets(
        ClockInPolicy policy,
        LegalEntity legalEntity,
        EmployeeRemoteWorkProfile? remoteProfile)
    {
        var targets = new List<LocationTarget>(2);
        if (TryCreateOfficeTarget(policy, legalEntity, out var officeTarget))
        {
            targets.Add(officeTarget);
        }

        if (remoteProfile is not null &&
            remoteProfile.TenantId == legalEntity.TenantId &&
            string.Equals(remoteProfile.Status, "active", StringComparison.Ordinal) &&
            TryCreateRemoteTarget(remoteProfile, out var remoteTarget))
        {
            targets.Add(remoteTarget);
        }

        return targets;
    }

    private static bool HasExpectedLocationTarget(
        string workArea,
        IReadOnlyList<LocationTarget> targets) =>
        workArea switch
        {
            "onsite" => targets.Any(target =>
                target.Source == "company_office"),
            "remote" => targets.Any(target =>
                target.Source == "remote_profile"),
            "either" => targets.Count > 0,
            "field" => true,
            _ => false
        };

    private static bool TryCreateOfficeTarget(
        ClockInPolicy policy,
        LegalEntity legalEntity,
        out LocationTarget target)
    {
        target = default!;
        var radius =
            policy.AllowedRadiusMeters ??
            legalEntity.OfficeAllowedRadiusMeters;
        if (legalEntity.OfficeLatitude is null ||
            legalEntity.OfficeLongitude is null ||
            radius is null)
        {
            return false;
        }

        target = new LocationTarget(
            legalEntity.Id,
            "company_office",
            legalEntity.OfficeLatitude.Value,
            legalEntity.OfficeLongitude.Value,
            radius.Value);
        return true;
    }

    private static bool TryCreateRemoteTarget(
        EmployeeRemoteWorkProfile profile,
        out LocationTarget target)
    {
        target = default!;
        if (string.IsNullOrWhiteSpace(profile.CoarseLocationJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(profile.CoarseLocationJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("latitude", out var latitude) ||
                !root.TryGetProperty("longitude", out var longitude) ||
                !latitude.TryGetDecimal(out var parsedLatitude) ||
                !longitude.TryGetDecimal(out var parsedLongitude))
            {
                return false;
            }

            target = new LocationTarget(
                profile.Id,
                "remote_profile",
                parsedLatitude,
                parsedLongitude,
                DefaultRemoteRadiusMeters);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsTrayEnabled(
        string workArea,
        ClockInPolicy policy) =>
        workArea switch
        {
            "onsite" => policy.OnsiteTrayEnabled,
            "remote" => policy.RemoteTrayEnabled,
            "either" => policy.EitherTrayEnabled,
            "field" => policy.FieldTrayEnabled,
            _ => false
        };

    private static bool AppliesToArea(string scope, string workArea) =>
        scope switch
        {
            "remote_only" => workArea is "remote" or "either",
            "onsite_only" => workArea is "onsite" or "either",
            "remote_and_onsite" => workArea is "remote" or "onsite" or "either",
            _ => false
        };

    private static DateTimeOffset? CalculateHardStop(
        DateOnly date,
        WorkScheduleDay? scheduleDay,
        TimeZoneInfo timezone)
    {
        if (scheduleDay?.EndTime is null)
            return null;

        var endDate = scheduleDay.IsOvernight
            ? date.AddDays(1)
            : date;
        var localEnd = DateTime.SpecifyKind(
            endDate.ToDateTime(scheduleDay.EndTime.Value),
            DateTimeKind.Unspecified);
        if (timezone.IsInvalidTime(localEnd))
            return null;

        var utc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static bool TryResolveTimezone(
        string? timezoneId,
        out TimeZoneInfo timezone)
    {
        timezone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timezoneId))
            return false;

        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static short ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday
            ? (short)7
            : (short)dayOfWeek;

    private static string NormalizeWorkArea(string? workArea) =>
        workArea?.Trim().ToLowerInvariant() switch
        {
            "remote" => "remote",
            "field" => "field",
            "hybrid" or "either" => "either",
            "on_site" or "onsite" => "onsite",
            _ => "onsite"
        };
}
