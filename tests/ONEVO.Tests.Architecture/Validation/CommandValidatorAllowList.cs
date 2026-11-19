namespace ONEVO.Tests.Architecture.Validation;

/// <summary>
/// Commands that take string input but do not have a validator yet (task 19 follow-up:
/// docs/Improvements/person-1-security-and-auth/task-19b Validation coverage for the remaining commands.md).
/// This list may only shrink. When you add a validator for one of these, delete its line here:
/// CommandValidatorCoverageArchitectureTests fails while a listed command has a validator. Never add a new command
/// here; give it a validator instead.
/// </summary>
internal static class CommandValidatorAllowList
{
    public static readonly string[] Commands =
    [
        "ONEVO.Application.Features.Conversations.Commands.OpenConversation.OpenConversationCommand",
        "ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage.ReactToConversationMessageCommand",
        "ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping.PreviewBulkOnboardingMappingCommand",
        "ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ResolveBulkOnboardingIssues.ResolveBulkOnboardingIssuesCommand",
        "ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch.UploadBulkOnboardingBatchCommand",
        "ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch.ValidateBulkOnboardingBatchCommand",
        "ONEVO.Application.Features.CoreHr.Employee.Commands.AddMyDocument.AddMyDocumentCommand",
        "ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget.AddDashboardWidgetCommand",
        "ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout.SaveDashboardLayoutCommand",
        "ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo.SetGlobalApplicationLogoCommand",
        "ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.UpdateGlobalApplication.UpdateGlobalApplicationCommand",
        "ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate.CreateConfigurationTemplateCommand",
        "ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata.UpdateConfigurationTemplateMetadataCommand",
        "ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite.AcceptPlatformManagerInviteCommand",
        "ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager.InvitePlatformManagerCommand",
        "ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions.UpdatePlatformRolePermissionsCommand",
        "ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease.CreateTrayReleaseCommand",
        "ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease.UpdateTrayReleaseCommand",
        "ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole.AdminCreateTenantRoleCommand",
        "ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate.ApplyRoleTemplateCommand",
        "ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate.CreateRoleTemplateCommand",
        "ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate.UpdateRoleTemplateCommand",
        "ONEVO.Application.Features.Inbox.Chats.SetChatIconCommand",
        "ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction.ExecuteInboxActionCommand",
        "ONEVO.Application.Features.Leave.Request.Commands.UploadLeaveDocument.UploadLeaveDocumentCommand",
        "ONEVO.Application.Features.Monitoring.Meetings.Commands.IngestMeetingSignals.IngestMeetingSignalsCommand",
        "ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot.SubmitPeriodicScreenshotCommand",
        "ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization.ApproveDeviceAuthorizationCommand",
        "ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests.ApproveDeviceChangeRequestCommand",
        "ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests.RejectDeviceChangeRequestCommand",
        "ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization.PollDeviceAuthorizationCommand",
        "ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation.ConfirmWorkLocationCommand",
        "ONEVO.Application.Features.OrgStructure.Commands.MoveReportingLines.MoveReportingLinesCommand",
        "ONEVO.Application.Features.Storage.File.Commands.UploadFile.UploadFileCommand",
        "ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests.ApproveLocationChangeRequestCommand",
        "ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests.CreateLocationChangeRequestCommand",
        "ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests.RejectLocationChangeRequestCommand",
        "ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests.TraySubmitLocationChangeRequestCommand",
        "ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest.DecideWorkApprovalRequestCommand",
        "ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset.UploadObjectiveAssetCommand",
        "ONEVO.Application.Features.WorkManagement.Sprints.Commands.CompleteSprint.CompleteSprintCommand",
        "ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint.StartSprintCommand",
        "ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason.AddClockingSessionReasonCommand",
        "ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason.AddPercentageLogReasonCommand",
        "ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction.AddTaskCommentReactionCommand",
        "ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskPendingUpload.CreateTaskPendingUploadCommand",
        "ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction.RemoveTaskCommentReactionCommand",
    ];
}
