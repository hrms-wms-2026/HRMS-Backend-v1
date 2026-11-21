using FluentValidation;
using ONEVO.Application.Common.Validation;

namespace ONEVO.Application.Features.TimeAttendance.Commands.AttendanceCorrections;

/// <summary>Task 19 bounds for attendance corrections. Column sizes: attendance_corrections.correction_type
/// nvarchar(30), reason nvarchar(255); notes and review_comment are nvarchar(max), capped at 2000 (the workflow's own
/// notes limit); a break's type is nvarchar(30) in the attendance read model. The workflow still checks the type
/// values, that times fall on the local work date, and that breaks do not overlap.</summary>
public static class AttendanceCorrectionInputLimits
{
    public const int CorrectionTypeMaxLength = 30;
    public const int ReasonMaxLength = 255;
    public const int NotesMaxLength = 2000;
    public const int ReviewCommentMaxLength = 2000;
    public const int BreakTypeMaxLength = 30;
    public const int MaxBreaks = 20;

    /// <summary>All requested times must fall on one local work date; a daylight-saving day is 25 hours long.</summary>
    public static readonly TimeSpan MaxSpan = TimeSpan.FromHours(25);
}

public sealed class AttendanceCorrectionInputBreakValidator : AbstractValidator<AttendanceCorrectionInputBreak>
{
    public AttendanceCorrectionInputBreakValidator()
    {
        RuleFor(x => x.BreakType).ValidName(AttendanceCorrectionInputLimits.BreakTypeMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.BreakStart, x => x.BreakEnd, AttendanceCorrectionInputLimits.MaxSpan);
    }
}

public sealed class PreviewAttendanceCorrectionCommandValidator : AbstractValidator<PreviewAttendanceCorrectionCommand>
{
    public PreviewAttendanceCorrectionCommandValidator()
    {
        RuleFor(x => x.CorrectionType).ValidName(AttendanceCorrectionInputLimits.CorrectionTypeMaxLength);
        RuleFor(x => x.Reason).ValidName(AttendanceCorrectionInputLimits.ReasonMaxLength);
        RuleFor(x => x.Notes).ValidName(AttendanceCorrectionInputLimits.NotesMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.RequestedClockInAt, x => x.RequestedClockOutAt, AttendanceCorrectionInputLimits.MaxSpan);
        RuleFor(x => x.RequestedBreaks)
            .Must(b => b is null || b.Count <= AttendanceCorrectionInputLimits.MaxBreaks)
            .WithMessage($"'{{PropertyName}}' can hold at most {AttendanceCorrectionInputLimits.MaxBreaks} entries.");
        RuleForEach(x => x.RequestedBreaks).SetValidator(new AttendanceCorrectionInputBreakValidator());
    }
}

public sealed class RequestAttendanceCorrectionCommandValidator : AbstractValidator<RequestAttendanceCorrectionCommand>
{
    public RequestAttendanceCorrectionCommandValidator()
    {
        RuleFor(x => x.CorrectionType).ValidName(AttendanceCorrectionInputLimits.CorrectionTypeMaxLength);
        RuleFor(x => x.Reason).ValidName(AttendanceCorrectionInputLimits.ReasonMaxLength);
        RuleFor(x => x.Notes).ValidName(AttendanceCorrectionInputLimits.NotesMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.RequestedClockInAt, x => x.RequestedClockOutAt, AttendanceCorrectionInputLimits.MaxSpan);
        RuleFor(x => x.RequestedBreaks)
            .Must(b => b is null || b.Count <= AttendanceCorrectionInputLimits.MaxBreaks)
            .WithMessage($"'{{PropertyName}}' can hold at most {AttendanceCorrectionInputLimits.MaxBreaks} entries.");
        RuleForEach(x => x.RequestedBreaks).SetValidator(new AttendanceCorrectionInputBreakValidator());
    }
}

public sealed class ApproveAttendanceCorrectionCommandValidator : AbstractValidator<ApproveAttendanceCorrectionCommand>
{
    public ApproveAttendanceCorrectionCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(AttendanceCorrectionInputLimits.ReviewCommentMaxLength, required: false);
        RuleFor(x => x.EditedValues!).ChildRules(edited =>
        {
            edited.RuleFor(x => x).ValidDateRange(x => x.ClockInAt, x => x.ClockOutAt, AttendanceCorrectionInputLimits.MaxSpan);
            edited.RuleFor(x => x.Breaks)
                .Must(b => b is null || b.Count <= AttendanceCorrectionInputLimits.MaxBreaks)
                .WithMessage($"'{{PropertyName}}' can hold at most {AttendanceCorrectionInputLimits.MaxBreaks} entries.");
            edited.RuleForEach(x => x.Breaks).SetValidator(new AttendanceCorrectionInputBreakValidator());
        }).When(x => x.EditedValues is not null);
    }
}

public sealed class RejectAttendanceCorrectionCommandValidator : AbstractValidator<RejectAttendanceCorrectionCommand>
{
    public RejectAttendanceCorrectionCommandValidator()
    {
        RuleFor(x => x.ReviewComment).ValidName(AttendanceCorrectionInputLimits.ReviewCommentMaxLength, required: false);
    }
}
