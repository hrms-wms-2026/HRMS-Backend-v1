using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// The 30-minute undo of an attendance decision (plan README §3.4). Approve and reject can be undone by the person
/// who decided, while they still hold attendance:approve; the request goes back to pending. Each kind adds what its
/// approval changed and what blocks undoing it.
/// </summary>
public abstract class AttendanceDecisionReverterBase : InboxDecisionReverterBase
{
    public const string NotUndoable = "This kind of decision cannot be undone from the Inbox.";

    private readonly ICurrentUser _currentUser;

    protected AttendanceDecisionReverterBase(IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ICurrentUser currentUser)
        : base(journal, unitOfWork, clock)
    {
        _currentUser = currentUser;
        Clock = clock;
    }

    protected IDateTimeProvider Clock { get; }

    public override string Source => AttendanceInboxKinds.Source;

    protected sealed override async Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(AttendanceInboxKinds.ApprovePermission))
            return Result.Forbidden(AttendanceInboxSourceProvider.MissingPermission);
        return decision.Action switch
        {
            AttendanceInboxActions.Approve => await CheckApprovalAsync(tenantId, decision, ct),
            AttendanceInboxActions.Reject => Result.Success(),
            _ => Result.UnprocessableEntity(NotUndoable),
        };
    }

    protected virtual Task<Result> CheckApprovalAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
        => Task.FromResult(Result.Success());

    protected static AttendanceDecisionSnapshot Before(InboxDecisionJournalEntry decision)
        => AttendanceDecisionSnapshot.Parse(decision.BeforeJson) ?? throw new InvalidOperationException("The decision has no before-state.");
}

/// <summary>Correction approved: the attendance day back as it was (times, minutes, status; breaks for a break
/// correction). Blocked when a newer correction was applied to the same day since.</summary>
public sealed class AttendanceCorrectionReverter : AttendanceDecisionReverterBase
{
    public const string NewerCorrection = "A newer correction was applied to this day, so this one can no longer be undone.";

    private readonly IAttendanceCorrectionRepository _corrections;
    private readonly IAttendanceReadRepository _attendance;
    private readonly AttendanceInboxSourceProvider _provider;

    public AttendanceCorrectionReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ICurrentUser currentUser,
        IAttendanceCorrectionRepository corrections, IAttendanceReadRepository attendance, AttendanceInboxSourceProvider provider)
        : base(journal, unitOfWork, clock, currentUser)
    {
        _corrections = corrections;
        _attendance = attendance;
        _provider = provider;
    }

    public override string Kind => AttendanceInboxKinds.Correction;

    protected override async Task<Result> CheckApprovalAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        var correction = await _corrections.GetByIdAsync(tenantId, decision.ItemId, ct);
        if (correction is null)
            return Result.NotFound("Attendance correction was not found.");
        var (sameDay, _) = await _corrections.ListMyAsync(tenantId, correction.EmployeeId, correction.WorkDate, correction.WorkDate,
            AttendanceCorrection.StatusApproved, 0, 100, ct);
        return sameDay.Any(c => c.Id != correction.Id && c.ReviewedAt > decision.DecidedAt)
            ? Result.Conflict(NewerCorrection)
            : Result.Success();
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var correction = (await _corrections.GetTrackedByIdAsync(tenantId, decision.ItemId, ct))!;
        if (decision.Action == AttendanceInboxActions.Approve && Before(decision).Record is { } was
            && await _attendance.GetTrackedRecordAsync(tenantId, correction.EmployeeId, correction.WorkDate, ct) is { } record)
        {
            record.ActualStart = was.ActualStart;
            record.ActualEnd = was.ActualEnd;
            record.BreakMinutes = was.BreakMinutes;
            record.WorkedMinutes = was.WorkedMinutes;
            record.LateMinutes = was.LateMinutes;
            record.Status = was.Status;
            record.UpdatedAt = Clock.UtcNow;
            if (correction.CorrectionType == AttendanceCorrection.TypeBreak)
            {
                var (from, to) = await _provider.DayWindowAsync(tenantId, correction.LegalEntityId, correction.WorkDate, ct);
                foreach (var current in await _attendance.ListBreaksAsync(tenantId, correction.EmployeeId, from, to, ct))
                    await _attendance.DeleteBreakAsync(current.Id, ct);
                foreach (var b in was.Breaks)
                    await _attendance.AddBreakAsync(new BreakRecord
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = correction.EmployeeId, BreakStart = b.BreakStart,
                        BreakEnd = b.BreakEnd, BreakType = b.BreakType ?? string.Empty, AutoDetected = false, CreatedAt = Clock.UtcNow,
                    }, ct);
            }
        }

        correction.Status = AttendanceCorrection.StatusPending;
        correction.ReviewedById = null;
        correction.ReviewedAt = null;
        correction.ReviewComment = null;
        correction.ApprovedClockInAt = null;
        correction.ApprovedClockOutAt = null;
        correction.ApprovedBreakJson = null;
        correction.UpdatedAt = Clock.UtcNow;
        await _corrections.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([correction.EmployeeId, callerEmployeeId]));
    }
}

/// <summary>Work mode approved: the requested mode and the day's planned mode back as they were. Blocked once the day
/// is past and the employee already worked it under the new mode.</summary>
public sealed class WorkModeChangeReverter : AttendanceDecisionReverterBase
{
    public const string AlreadyWorked = "The day has passed and was worked under the new work mode, so this can no longer be undone.";

    private readonly IWorkAreaChangeRequestRepository _requests;
    private readonly IAttendanceReadRepository _attendance;

    public WorkModeChangeReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ICurrentUser currentUser,
        IWorkAreaChangeRequestRepository requests, IAttendanceReadRepository attendance)
        : base(journal, unitOfWork, clock, currentUser)
    {
        _requests = requests;
        _attendance = attendance;
    }

    public override string Kind => AttendanceInboxKinds.WorkModeChange;

    protected override async Task<Result> CheckApprovalAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        var request = await _requests.GetByIdAsync(tenantId, decision.ItemId, ct);
        if (request is null)
            return Result.NotFound("Work-area change request was not found.");
        if (request.Date >= Clock.Today)
            return Result.Success();
        var record = await _attendance.GetRecordAsync(tenantId, request.EmployeeId, request.Date, ct);
        return record?.ActualStart is not null ? Result.Conflict(AlreadyWorked) : Result.Success();
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var request = (await _requests.GetTrackedByIdAsync(tenantId, decision.ItemId, ct))!;
        if (decision.Action == AttendanceInboxActions.Approve)
        {
            var before = Before(decision);
            if (before.WorkModeId is { } requestedId)
            {
                request.RequestedWorkModeId = requestedId;
                request.RequestedWorkModeName = before.WorkModeName ?? request.RequestedWorkModeName;
            }
            if (await _attendance.GetTrackedRecordAsync(tenantId, request.EmployeeId, request.Date, ct) is { } record)
            {
                record.ExpectedWorkModeId = before.RecordWorkModeId;
                record.ExpectedWorkModeName = before.RecordWorkModeName;
                record.UpdatedAt = Clock.UtcNow;
            }
        }
        request.Status = WorkAreaChangeRequest.StatusPending;
        request.ReviewedById = null;
        request.ReviewedAt = null;
        request.ReviewComment = null;
        await _requests.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([request.EmployeeId, callerEmployeeId]));
    }
}

/// <summary>Location approved: back to pending; when the employee already applied it, the previous work location
/// comes back - unless another location change was applied since.</summary>
public sealed class LocationChangeReverter : AttendanceDecisionReverterBase
{
    public const string NewerLocation = "Another work location was applied after this one, so it can no longer be undone.";

    private readonly ILocationChangeRequestRepository _requests;
    private readonly IEmployeeWorkLocationRepository _workLocations;

    public LocationChangeReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ICurrentUser currentUser,
        ILocationChangeRequestRepository requests, IEmployeeWorkLocationRepository workLocations)
        : base(journal, unitOfWork, clock, currentUser)
    {
        _requests = requests;
        _workLocations = workLocations;
    }

    public override string Kind => AttendanceInboxKinds.LocationChange;

    protected override async Task<Result> CheckApprovalAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        var request = await _requests.GetByIdAsync(tenantId, decision.ItemId, ct);
        if (request is null)
            return Result.NotFound("Location change request was not found.");
        if (request.Status != LocationChangeRequest.StatusApplied)
            return Result.Success();
        var current = await _workLocations.GetByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        return current is not null && current.UpdatedAt == request.AppliedAt ? Result.Success() : Result.Conflict(NewerLocation);
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var request = (await _requests.GetTrackedByIdAsync(tenantId, decision.ItemId, ct))!;
        if (request.Status == LocationChangeRequest.StatusApplied
            && await _workLocations.GetByEmployeeIdAsync(tenantId, request.EmployeeId, ct) is { } current)
        {
            if (Before(decision).WorkLocation is { } was)
            {
                current.Latitude = was.Latitude;
                current.Longitude = was.Longitude;
                current.AccuracyMeters = was.AccuracyMeters;
                current.UpdatedAt = Clock.UtcNow;
                _workLocations.Update(current);
            }
            else
            {
                // The applied request created the employee's first work location.
                _workLocations.Remove(current);
            }
        }
        request.Status = LocationChangeRequest.StatusPending;
        request.AppliedAt = null;
        request.ReviewedById = null;
        request.ReviewedAt = null;
        request.ReviewComment = null;
        await _requests.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([request.EmployeeId, callerEmployeeId]));
    }
}

/// <summary>Device change approved: the old device registration is active again and the request back to pending.
/// Blocked once the new device has been used (signed in or reported activity) since the approval.</summary>
public sealed class DeviceChangeReverter : AttendanceDecisionReverterBase
{
    public const string NewDeviceUsed = "The new device has already been used, so this approval can no longer be undone.";

    private readonly IDeviceChangeRequestRepository _requests;
    private readonly ITrayActivationRepository _tray;
    private readonly IEmployeeRepository _employees;

    public DeviceChangeReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, ICurrentUser currentUser,
        IDeviceChangeRequestRepository requests, ITrayActivationRepository tray, IEmployeeRepository employees)
        : base(journal, unitOfWork, clock, currentUser)
    {
        _requests = requests;
        _tray = tray;
        _employees = employees;
    }

    public override string Kind => AttendanceInboxKinds.DeviceChange;

    protected override async Task<Result> CheckApprovalAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        var request = await _requests.GetTrackedByIdAsync(tenantId, decision.ItemId, ct);
        if (request is null)
            return Result.NotFound("Device change request was not found.");
        var employee = await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result.Success();
        var active = await _tray.FindActiveDeviceForUserAsync(employee.UserId, tenantId, ct);
        var usedSince = active is not null && active.Id != request.CurrentDeviceRegistrationId
            && (active.ActivatedAt > decision.DecidedAt || active.LastSeenAt > decision.DecidedAt);
        return usedSince ? Result.Conflict(NewDeviceUsed) : Result.Success();
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var request = (await _requests.GetTrackedByIdAsync(tenantId, decision.ItemId, ct))!;
        if (decision.Action == AttendanceInboxActions.Approve && Before(decision) is { OldDeviceRegistrationId: { } oldId, OldDeviceActive: true })
            await _tray.ReactivateDeviceAsync(oldId, ct);
        request.Status = DeviceChangeRequest.StatusPending;
        request.ReviewedById = null;
        request.ReviewedAt = null;
        request.ReviewComment = null;
        await _requests.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([request.EmployeeId, callerEmployeeId]));
    }
}
