using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;
using ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;

namespace ONEVO.Application.Features.Monitoring.Validation;

/// <summary>
/// Task 19b. Device input from the tray agent and the reviews of it.
/// <list type="bullet">
/// <item>The device code and fingerprint follow the 512-character bound the other tray validators
/// (StartDeviceAuthorization, RefreshTrayToken) already use; both are hashed before storage. The user code is 8
/// characters; 32 leaves room for a typed separator and spaces.</item>
/// <item>Meeting signals: a process name is a Windows executable name (at most 260 characters with its path). The
/// tray uploads its offline backlog in one call, so the batch cap of 10,000 samples (about a week of one-minute
/// samples) is a request bound, not a sampling limit.</item>
/// <item>daily_work_location_confirmations.location_type is nvarchar(20). Coordinates must be on the globe and
/// accuracy not negative.</item>
/// <item>Change reasons are nvarchar(max); 2,000 matches the other decision notes. Review comments are nvarchar(max)
/// too, and the inbox approve and reject dialogs pass a note of up to 4,000 characters straight into them, so they
/// get 4,000.</item>
/// </list>
/// The handlers keep the required checks, known location types and the state rules.
/// </summary>
public static class MonitoringLimits
{
    public const int DeviceCodeMaxLength = 512;
    public const int DeviceFingerprintMaxLength = 512;
    public const int UserCodeMaxLength = 32;
    public const int ProcessNameMaxLength = 260;
    public const int MaxSignalsPerBatch = 10_000;
    public const int LocationTypeMaxLength = 20;
    public const int NoteMaxLength = 2_000;
    public const int ReviewCommentMaxLength = 4_000;
}

public sealed class PollDeviceAuthorizationCommandValidator : AbstractValidator<PollDeviceAuthorizationCommand>
{
    public PollDeviceAuthorizationCommandValidator()
    {
        RuleFor(x => x.DeviceCode).ValidName(MonitoringLimits.DeviceCodeMaxLength, required: false);
        RuleFor(x => x.DeviceFingerprint).ValidName(MonitoringLimits.DeviceFingerprintMaxLength, required: false);
    }
}

public sealed class ApproveDeviceAuthorizationCommandValidator : AbstractValidator<ApproveDeviceAuthorizationCommand>
{
    public ApproveDeviceAuthorizationCommandValidator()
    {
        RuleFor(x => x.UserCode).ValidName(MonitoringLimits.UserCodeMaxLength, required: false);
    }
}

public sealed class ApproveDeviceChangeRequestCommandValidator : AbstractValidator<ApproveDeviceChangeRequestCommand>
{
    public ApproveDeviceChangeRequestCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(MonitoringLimits.ReviewCommentMaxLength, required: false);
    }
}

public sealed class RejectDeviceChangeRequestCommandValidator : AbstractValidator<RejectDeviceChangeRequestCommand>
{
    public RejectDeviceChangeRequestCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(MonitoringLimits.ReviewCommentMaxLength, required: false);
    }
}

public sealed class IngestMeetingSignalsCommandValidator : AbstractValidator<IngestMeetingSignalsCommand>
{
    public IngestMeetingSignalsCommandValidator()
    {
        RuleFor(x => x.Signals)
            .Must(s => s is null || s.Count <= MonitoringLimits.MaxSignalsPerBatch)
            .WithMessage($"'{{PropertyName}}' can hold at most {MonitoringLimits.MaxSignalsPerBatch} entries.");
        RuleForEach(x => x.Signals)
            .Must(s => s is null || s.ProcessName is null || s.ProcessName.Length <= MonitoringLimits.ProcessNameMaxLength)
            .WithMessage($"A process name must be {MonitoringLimits.ProcessNameMaxLength} characters or fewer.");
    }
}

/// <summary>Coordinates shared by the work-location confirmation and the location change requests.</summary>
internal static class CoordinateRules
{
    public static bool IsLatitude(double? value) => value is null || (value >= -90 && value <= 90);
    public static bool IsLongitude(double? value) => value is null || (value >= -180 && value <= 180);
    public static bool IsAccuracy(double? value) => value is null || value >= 0;
}

public sealed class ConfirmWorkLocationCommandValidator : AbstractValidator<ConfirmWorkLocationCommand>
{
    public ConfirmWorkLocationCommandValidator()
    {
        RuleFor(x => x.LocationType).ValidName(MonitoringLimits.LocationTypeMaxLength, required: false);
        RuleFor(x => x.Latitude).Must(CoordinateRules.IsLatitude).WithMessage("'{PropertyName}' must be between -90 and 90.");
        RuleFor(x => x.Longitude).Must(CoordinateRules.IsLongitude).WithMessage("'{PropertyName}' must be between -180 and 180.");
        RuleFor(x => x.AccuracyMeters).Must(CoordinateRules.IsAccuracy).WithMessage("'{PropertyName}' must not be negative.");
    }
}

public sealed class CreateLocationChangeRequestCommandValidator : AbstractValidator<CreateLocationChangeRequestCommand>
{
    public CreateLocationChangeRequestCommandValidator()
    {
        RuleFor(x => x.Reason).ValidName(MonitoringLimits.NoteMaxLength, required: false);
        RuleFor(x => x.Latitude).Must(v => CoordinateRules.IsLatitude(v)).WithMessage("'{PropertyName}' must be between -90 and 90.");
        RuleFor(x => x.Longitude).Must(v => CoordinateRules.IsLongitude(v)).WithMessage("'{PropertyName}' must be between -180 and 180.");
        RuleFor(x => x.AccuracyMeters).Must(CoordinateRules.IsAccuracy).WithMessage("'{PropertyName}' must not be negative.");
    }
}

public sealed class TraySubmitLocationChangeRequestCommandValidator : AbstractValidator<TraySubmitLocationChangeRequestCommand>
{
    public TraySubmitLocationChangeRequestCommandValidator()
    {
        RuleFor(x => x.Reason).ValidName(MonitoringLimits.NoteMaxLength, required: false);
        RuleFor(x => x.Latitude).Must(v => CoordinateRules.IsLatitude(v)).WithMessage("'{PropertyName}' must be between -90 and 90.");
        RuleFor(x => x.Longitude).Must(v => CoordinateRules.IsLongitude(v)).WithMessage("'{PropertyName}' must be between -180 and 180.");
        RuleFor(x => x.AccuracyMeters).Must(CoordinateRules.IsAccuracy).WithMessage("'{PropertyName}' must not be negative.");
    }
}

public sealed class ApproveLocationChangeRequestCommandValidator : AbstractValidator<ApproveLocationChangeRequestCommand>
{
    public ApproveLocationChangeRequestCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(MonitoringLimits.ReviewCommentMaxLength, required: false);
    }
}

public sealed class RejectLocationChangeRequestCommandValidator : AbstractValidator<RejectLocationChangeRequestCommand>
{
    public RejectLocationChangeRequestCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(MonitoringLimits.ReviewCommentMaxLength, required: false);
    }
}
