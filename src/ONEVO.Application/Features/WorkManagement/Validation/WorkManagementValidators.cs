using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.OrgStructure.Commands.MoveReportingLines;
using ONEVO.Application.Features.OrgStructure.Commands.PreviewReportingLineMove;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;

namespace ONEVO.Application.Features.WorkManagement.Validation;

/// <summary>
/// Task 19b. Approvals, sprints, reasons, reactions and reporting-line moves.
/// <list type="bullet">
/// <item>Approval comments come from the inbox dialogs, which allow 4,000 characters, so they get 4,000.</item>
/// <item>Sprint goals, clocking-session and percentage-log reasons and reporting-line move
/// reasons are nvarchar(max); 2,000 matches the other decision notes.</item>
/// <item>An edited approval payload is the full form as JSON (for a meeting it can carry a 50,000-character
/// description and 1,000 guest emails), so its cap is 1,000,000 characters. The handler still parses and checks
/// it.</item>
/// <item>A sprint may run at most 366 days; end before start is now a 400.</item>
/// <item>task_comment_reactions.emoji is nvarchar(32), the same as ConversationRules.MaxEmojiLength.</item>
/// <item>A reporting-line move touches at most 1,000 people per request, the meeting bulk-invite bound.</item>
/// </list>
/// </summary>
public static class WorkManagementLimits
{
    public const int NoteMaxLength = 2_000;
    public const int ApprovalCommentMaxLength = 4_000;
    public const int EditedPayloadMaxLength = 1_000_000;
    public const int MaxSprintDays = 366;
    public const int EmojiMaxLength = ConversationRules.MaxEmojiLength;
    public const int MaxEmployeesPerMove = 1_000;
}

public sealed class DecideWorkApprovalRequestCommandValidator : AbstractValidator<DecideWorkApprovalRequestCommand>
{
    public DecideWorkApprovalRequestCommandValidator()
    {
        RuleFor(x => x.EditedPayloadJson).ValidName(WorkManagementLimits.EditedPayloadMaxLength, required: false);
        RuleFor(x => x.Comment).ValidName(WorkManagementLimits.ApprovalCommentMaxLength, required: false);
    }
}

public sealed class StartSprintCommandValidator : AbstractValidator<StartSprintCommand>
{
    public StartSprintCommandValidator()
    {
        RuleFor(x => x.Goal).ValidName(WorkManagementLimits.NoteMaxLength, required: false);
        RuleFor(x => x).ValidDateRange(x => x.StartDate, x => x.EndDate, WorkManagementLimits.MaxSprintDays);
    }
}

public sealed class CompleteSprintCommandValidator : AbstractValidator<CompleteSprintCommand>
{
    public CompleteSprintCommandValidator()
    {
        RuleFor(x => x.Disposition).ValidName(InputLimits.KeyMaxLength, required: false);
    }
}

public sealed class AddClockingSessionReasonCommandValidator : AbstractValidator<AddClockingSessionReasonCommand>
{
    public AddClockingSessionReasonCommandValidator()
    {
        RuleFor(x => x.Reason).ValidName(WorkManagementLimits.NoteMaxLength, required: false);
    }
}

public sealed class AddPercentageLogReasonCommandValidator : AbstractValidator<AddPercentageLogReasonCommand>
{
    public AddPercentageLogReasonCommandValidator()
    {
        RuleFor(x => x.Reason).ValidName(WorkManagementLimits.NoteMaxLength, required: false);
    }
}

public sealed class AddTaskCommentReactionCommandValidator : AbstractValidator<AddTaskCommentReactionCommand>
{
    public AddTaskCommentReactionCommandValidator()
    {
        RuleFor(x => x.Emoji).ValidName(WorkManagementLimits.EmojiMaxLength, required: false);
    }
}

public sealed class RemoveTaskCommentReactionCommandValidator : AbstractValidator<RemoveTaskCommentReactionCommand>
{
    public RemoveTaskCommentReactionCommandValidator()
    {
        RuleFor(x => x.Emoji).ValidName(WorkManagementLimits.EmojiMaxLength, required: false);
    }
}

public sealed class MoveReportingLinesCommandValidator : AbstractValidator<MoveReportingLinesCommand>
{
    public MoveReportingLinesCommandValidator()
    {
        RuleFor(x => x.SelectedEmployeeIds)
            .Must(ids => ids is null || ids.Count <= WorkManagementLimits.MaxEmployeesPerMove)
            .WithMessage($"'{{PropertyName}}' can hold at most {WorkManagementLimits.MaxEmployeesPerMove} entries.");
        RuleFor(x => x.Reason).ValidName(WorkManagementLimits.NoteMaxLength, required: false);
    }
}

/// <summary>Not a string command, so the coverage test cannot see it; the task names it for its id list.</summary>
public sealed class PreviewReportingLineMoveCommandValidator : AbstractValidator<PreviewReportingLineMoveCommand>
{
    public PreviewReportingLineMoveCommandValidator()
    {
        RuleFor(x => x.SelectedEmployeeIds)
            .Must(ids => ids is null || ids.Count <= WorkManagementLimits.MaxEmployeesPerMove)
            .WithMessage($"'{{PropertyName}}' can hold at most {WorkManagementLimits.MaxEmployeesPerMove} entries.");
    }
}
