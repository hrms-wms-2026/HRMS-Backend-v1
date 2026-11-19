using FluentValidation.TestHelper;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage;
using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;
using ONEVO.Application.Features.Inbox.Validation;
using ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;
using ONEVO.Application.Features.Monitoring.Validation;
using ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;
using ONEVO.Application.Features.OrgStructure.Commands.MoveReportingLines;
using ONEVO.Application.Features.OrgStructure.Commands.PreviewReportingLineMove;
using ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint;
using ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: tray and monitoring input, approvals and reasons, chats, the inbox and the dashboard.</summary>
public sealed class DeviceApprovalAndChatValidatorTests
{
    // ---- Tray and monitoring ----

    [Fact]
    public void DeviceAuthorization_BoundsCodesAndFingerprint()
    {
        new PollDeviceAuthorizationCommandValidator().TestValidate(new PollDeviceAuthorizationCommand(S(512), S(512))).ShouldNotHaveAnyValidationErrors();
        var poll = new PollDeviceAuthorizationCommandValidator().TestValidate(new PollDeviceAuthorizationCommand(S(513), S(513)));
        poll.ShouldHaveValidationErrorFor(x => x.DeviceCode);
        poll.ShouldHaveValidationErrorFor(x => x.DeviceFingerprint);
        new ApproveDeviceAuthorizationCommandValidator().TestValidate(new ApproveDeviceAuthorizationCommand(Guid.NewGuid(), "ABCD-EFGH")).ShouldNotHaveAnyValidationErrors();
        new ApproveDeviceAuthorizationCommandValidator().TestValidate(new ApproveDeviceAuthorizationCommand(Guid.NewGuid(), S(33)))
            .ShouldHaveValidationErrorFor(x => x.UserCode);
    }

    [Fact]
    public void DeviceChangeReviews_CommentUpTo4000()
    {
        new ApproveDeviceChangeRequestCommandValidator().TestValidate(new ApproveDeviceChangeRequestCommand(Guid.NewGuid(), S(4_000))).ShouldNotHaveAnyValidationErrors();
        new RejectDeviceChangeRequestCommandValidator().TestValidate(new RejectDeviceChangeRequestCommand(Guid.NewGuid(), S(4_001)))
            .ShouldHaveValidationErrorFor(x => x.ReviewComment);
    }

    [Fact]
    public void MeetingSignals_BoundBatchAndProcessName()
    {
        var validator = new IngestMeetingSignalsCommandValidator();
        validator.TestValidate(new IngestMeetingSignalsCommand
        {
            Signals = [new MeetingSignalItem { CapturedAt = DateTimeOffset.UtcNow, ProcessName = S(260) }, new MeetingSignalItem()],
        }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new IngestMeetingSignalsCommand { Signals = [new MeetingSignalItem { ProcessName = S(261) }] })
            .ShouldHaveValidationErrorFor("Signals[0]");
        validator.TestValidate(new IngestMeetingSignalsCommand { Signals = Enumerable.Range(0, 10_001).Select(_ => new MeetingSignalItem()).ToList() })
            .ShouldHaveValidationErrorFor(x => x.Signals);
    }

    [Fact]
    public void Locations_BoundTypeReasonAndCoordinates()
    {
        new ConfirmWorkLocationCommandValidator().TestValidate(new ConfirmWorkLocationCommand("office", 6.9, 79.8, 15)).ShouldNotHaveAnyValidationErrors();
        new ConfirmWorkLocationCommandValidator().TestValidate(new ConfirmWorkLocationCommand("remote", null, null, null)).ShouldNotHaveAnyValidationErrors();
        var confirm = new ConfirmWorkLocationCommandValidator().TestValidate(new ConfirmWorkLocationCommand(S(21), 91, -181, -1));
        confirm.ShouldHaveValidationErrorFor(x => x.LocationType);
        confirm.ShouldHaveValidationErrorFor(x => x.Latitude);
        confirm.ShouldHaveValidationErrorFor(x => x.Longitude);
        confirm.ShouldHaveValidationErrorFor(x => x.AccuracyMeters);

        new CreateLocationChangeRequestCommandValidator().TestValidate(new CreateLocationChangeRequestCommand(-90, 180, null, S(2_000)))
            .ShouldNotHaveAnyValidationErrors();
        new TraySubmitLocationChangeRequestCommandValidator().TestValidate(new TraySubmitLocationChangeRequestCommand(0, 0, 5, S(2_001)))
            .ShouldHaveValidationErrorFor(x => x.Reason);
        new ApproveLocationChangeRequestCommandValidator().TestValidate(new ApproveLocationChangeRequestCommand(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
        new RejectLocationChangeRequestCommandValidator().TestValidate(new RejectLocationChangeRequestCommand(Guid.NewGuid(), S(4_001)))
            .ShouldHaveValidationErrorFor(x => x.ReviewComment);
    }

    // ---- Approvals, sprints, reasons, reactions, reporting lines ----

    [Fact]
    public void WorkApprovalDecision_BoundsPayloadAndComment()
    {
        var validator = new DecideWorkApprovalRequestCommandValidator();
        validator.TestValidate(new DecideWorkApprovalRequestCommand(Guid.NewGuid(), default, "{}", S(4_000))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new DecideWorkApprovalRequestCommand(Guid.NewGuid(), default, S(1_000_001), S(4_001)));
        result.ShouldHaveValidationErrorFor(x => x.EditedPayloadJson);
        result.ShouldHaveValidationErrorFor(x => x.Comment);
    }

    [Fact]
    public void Sprints_BoundGoalSpanAndDisposition()
    {
        var day = new DateOnly(2026, 12, 1);
        var validator = new StartSprintCommandValidator();
        validator.TestValidate(new StartSprintCommand(Guid.NewGuid(), day, day.AddDays(14), "Ship it")).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new StartSprintCommand(Guid.NewGuid(), day, day.AddDays(-1), null)).ShouldHaveValidationErrorFor(x => x.EndDate);
        validator.TestValidate(new StartSprintCommand(Guid.NewGuid(), day, day.AddDays(367), null)).ShouldHaveValidationErrorFor(x => x.EndDate);
        validator.TestValidate(new StartSprintCommand(Guid.NewGuid(), day, day.AddDays(1), S(2_001))).ShouldHaveValidationErrorFor(x => x.Goal);
        new CompleteSprintCommandValidator().TestValidate(new CompleteSprintCommand(Guid.NewGuid(), S(101), null)).ShouldHaveValidationErrorFor(x => x.Disposition);
    }

    [Fact]
    public void ReasonsAndReactions_AreBounded()
    {
        new AddClockingSessionReasonCommandValidator().TestValidate(new AddClockingSessionReasonCommand(Guid.NewGuid(), S(2_000))).ShouldNotHaveAnyValidationErrors();
        new AddClockingSessionReasonCommandValidator().TestValidate(new AddClockingSessionReasonCommand(Guid.NewGuid(), S(2_001)))
            .ShouldHaveValidationErrorFor(x => x.Reason);
        new AddPercentageLogReasonCommandValidator().TestValidate(new AddPercentageLogReasonCommand(Guid.NewGuid(), S(2_001)))
            .ShouldHaveValidationErrorFor(x => x.Reason);
        new AddTaskCommentReactionCommandValidator().TestValidate(new AddTaskCommentReactionCommand(Guid.NewGuid(), "👍")).ShouldNotHaveAnyValidationErrors();
        new RemoveTaskCommentReactionCommandValidator().TestValidate(new RemoveTaskCommentReactionCommand(Guid.NewGuid(), S(33)))
            .ShouldHaveValidationErrorFor(x => x.Emoji);
    }

    [Fact]
    public void ReportingLineMoves_CapPeopleAndReason()
    {
        new MoveReportingLinesCommandValidator().TestValidate(new MoveReportingLinesCommand(Guid.NewGuid(), Ids(1_000).ToList(), Guid.NewGuid(), S(2_000)))
            .ShouldNotHaveAnyValidationErrors();
        var move = new MoveReportingLinesCommandValidator().TestValidate(new MoveReportingLinesCommand(Guid.NewGuid(), Ids(1_001).ToList(), Guid.NewGuid(), S(2_001)));
        move.ShouldHaveValidationErrorFor(x => x.SelectedEmployeeIds);
        move.ShouldHaveValidationErrorFor(x => x.Reason);
        new PreviewReportingLineMoveCommandValidator().TestValidate(new PreviewReportingLineMoveCommand(Guid.NewGuid(), Ids(1_001).ToList(), Guid.NewGuid()))
            .ShouldHaveValidationErrorFor(x => x.SelectedEmployeeIds);
    }

    // ---- Chats, inbox and dashboard ----

    [Fact]
    public void ChatsAndInbox_BoundKeysEmojiAndIcon()
    {
        new OpenConversationCommandValidator().TestValidate(new OpenConversationCommand("leave", "request", Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
        var open = new OpenConversationCommandValidator().TestValidate(new OpenConversationCommand(S(41), S(41), Guid.NewGuid()));
        open.ShouldHaveValidationErrorFor(x => x.Source);
        open.ShouldHaveValidationErrorFor(x => x.Kind);

        var action = new ExecuteInboxActionCommandValidator().TestValidate(new ExecuteInboxActionCommand(S(41), "k", Guid.NewGuid(), S(101), null));
        action.ShouldHaveValidationErrorFor(x => x.Source);
        action.ShouldHaveValidationErrorFor(x => x.Action);

        new ReactToConversationMessageCommandValidator().TestValidate(new ReactToConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), null))
            .ShouldNotHaveAnyValidationErrors();
        new ReactToConversationMessageCommandValidator().TestValidate(new ReactToConversationMessageCommand(Guid.NewGuid(), Guid.NewGuid(), S(33)))
            .ShouldHaveValidationErrorFor(x => x.Emoji);

        new SetChatIconCommandValidator().TestValidate(new SetChatIconCommand(Guid.NewGuid(), "lucide:users", "#112233FF")).ShouldNotHaveAnyValidationErrors();
        var icon = new SetChatIconCommandValidator().TestValidate(new SetChatIconCommand(Guid.NewGuid(), S(65), S(10)));
        icon.ShouldHaveValidationErrorFor(x => x.Icon);
        icon.ShouldHaveValidationErrorFor(x => x.Color);
    }

    [Fact]
    public void Dashboard_BoundsWidgetKeyBreakpointAndLayoutSize()
    {
        new AddDashboardWidgetCommandValidator().TestValidate(new AddDashboardWidgetCommand(Guid.NewGuid(), "leave.balance", 1, null)).ShouldNotHaveAnyValidationErrors();
        new AddDashboardWidgetCommandValidator().TestValidate(new AddDashboardWidgetCommand(Guid.NewGuid(), S(101), 1, null))
            .ShouldHaveValidationErrorFor(x => x.WidgetKey);

        var widgets = Enumerable.Range(0, 200).Select(i => new LayoutItemDto(Guid.NewGuid(), 0, i, 1, 1)).ToList();
        new SaveDashboardLayoutCommandValidator().TestValidate(new SaveDashboardLayoutCommand(Guid.NewGuid(), "desktop", 1, widgets)).ShouldNotHaveAnyValidationErrors();
        var result = new SaveDashboardLayoutCommandValidator().TestValidate(new SaveDashboardLayoutCommand(Guid.NewGuid(), S(21), 1,
            [.. widgets, new LayoutItemDto(Guid.NewGuid(), 0, 0, 1, 1)]));
        result.ShouldHaveValidationErrorFor(x => x.Breakpoint);
        result.ShouldHaveValidationErrorFor(x => x.Widgets);
    }
}
