# Task 19b: Validation coverage for the remaining commands

| | |
|---|---|
| Priority | follow-up to task 19 |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 2 to 3 days (can be split by module) |
| Area | Security, Code quality |
| Depends on | task 19 (shared rules and the architecture test) |

## What we found

Task 19 added validators for the 30 highest-risk commands and an architecture test,
`tests/ONEVO.Tests.Architecture/Validation/CommandValidatorCoverageArchitectureTests.cs`. The test fails for any
command that takes string input and has no `AbstractValidator<TCommand>`. The 108 commands below don't have a
validator yet. They are listed in `CommandValidatorAllowList.cs`.

The list only shrinks. When you add a validator, delete that command's line from the allow-list, or the test
fails. Never add a new command to the allow-list. Give the new command a validator instead.

## What to do

1. Work through the list below, highest risk first. Use the shared rules in
   `ONEVO.Application.Common.Validation.ValidationRuleExtensions`: `ValidName`, `ValidRichText`, `ValidIdList`,
   `ValidDateRange` and `ValidPageRequest`.
2. Same rules as task 19:
   - Every string's maximum length matches its EF column. Read the configuration in
     `src/ONEVO.Infrastructure/Persistence/Configurations`. When the column is `nvarchar(max)`, choose a cap and
     write down why.
   - Every id list gets a maximum count.
   - Every date range gets a maximum span.
   - User-written rich text uses `ValidRichText`, which checks `RichTextRules.HasActiveContent`.
3. Leave required checks and business rules in the handler when the handler already returns a friendlier 422 for
   them. The validator is a security bound, not a second copy of the business rules.
4. Add unit tests for each validator, then remove the command from the allow-list.

### Highest risk first

1. **DevPlatform support tickets and announcements** (user-written text that admins and tenants see):
   `AddSupportTicketCommentCommand`, `CreateSupportTicketCommand`, `UpdateSupportTicketCommand`,
   `UpdateSupportTicketStatusCommand`, `UploadSupportTicketAttachmentCommand`, `AddTenantSupportTicketCommentCommand`,
   `CreateTenantSupportTicketCommand`, `UpdateTenantSupportTicketCommand`, `UploadTenantSupportTicketAttachmentCommand`,
   `CreatePlatformAnnouncementCommand`, `UpdatePlatformAnnouncementCommand`. `SupportTicketRules` already checks rich
   text in the handlers, so the validators add column limits and list caps.
2. **Legal documents** (rich HTML shown to every tenant): `CreateLegalDocumentVersionCommand`,
   `UpdateLegalDocumentVersionCommand`, `PublishLegalDocumentVersionCommand`.
3. **Meetings** (titles, guest emails, attendee lists, dates): `CreateMeetingCommand`, `EditMeetingCommand`,
   `MoveMeetingCommand`, `CancelMeetingCommand`, `AddOccurrenceAttendeesCommand`, `AddSeriesAttendeesCommand`,
   `RemoveOccurrenceGuestCommand`, `RemoveSeriesGuestCommand`, `RespondToMeetingCommand`, `CreateMeetingLinkCommand`.
   `CancelMeetingCommand` has no reason field. Its `Scope` is already checked by `MeetingCancelService`.
4. **Personal calendar**: `CreateCalendarEventCommand` and `UpdateCalendarEventCommand` in the Calendar module. These
   are not the WorkManagement ones, which already have validators. Also `CreateEventMeetingCommand`,
   `RemoveCalendarEventGuestCommand`, `RespondToCalendarEventCommand`, `UpdateHolidayCalendarSettingsCommand`.
   Match the limits task 19 used for `EditRecurringOccurrenceCommand`.
5. **Notification templates** (templates are rendered into emails): `CreateNotificationTemplateCommand`,
   `SaveNotificationTemplateDraftCommand`, `SendTestNotificationCommand`, `UpdateNotificationSettingCommand`,
   `ResetNotificationSettingCommand`.
6. **Credentials and secrets** (bound the sizes, never log the values): `CreatePaymentGatewayCommand`,
   `RotatePaymentGatewayCredentialCommand`, `UpdatePaymentGatewayMetadataCommand`, `VerifyGatewayCredentialsCommand`,
   `ConfigurePlatformOAuthAppCommand`, `RotatePlatformOAuthAppSecretCommand`, `SetPlatformOAuthAppActivationCommand`,
   `ValidatePlatformOAuthAppConfigCommand`, `CreatePlatformServiceKeyCommand`, `RotatePlatformServiceKeyCommand`,
   `SetPlatformServiceKeyActivationCommand`, `UpdatePlatformServiceKeyCommand`, `VerifyPlatformServiceKeyCommand`,
   `UpsertOwnUserIntegrationConnectionCommand`, `UpsertTenantIntegrationCredentialCommand`,
   `DisconnectOwnUserIntegrationCommand`, `StartGitHubUserOAuthCommand`, `CompleteGitHubUserOAuthCommand`,
   `StartCalendarConnectionCommand`, `CompleteCalendarConnectionCommand`, `UpdateCalendarConnectionCommand`.
7. **Bulk onboarding** (file mappings and issue fixes): `PreviewBulkOnboardingMappingCommand`,
   `ValidateBulkOnboardingBatchCommand`, `ResolveBulkOnboardingIssuesCommand`, `UploadBulkOnboardingBatchCommand`.
8. **Uploads** (file name and content type): `UploadFileCommand`, `AddMyDocumentCommand`, `UploadLeaveDocumentCommand`,
   `UploadObjectiveAssetCommand`, `CreateTaskPendingUploadCommand`, `SetGlobalApplicationLogoCommand`.
9. **Platform access and roles**: `InvitePlatformManagerCommand`, `AcceptPlatformManagerInviteCommand`,
   `UpdatePlatformRolePermissionsCommand`, `AdminCreateTenantRoleCommand`, `ApplyRoleTemplateCommand`,
   `CreateRoleTemplateCommand`, `UpdateRoleTemplateCommand`.
10. **Tray and monitoring** (device input): `IngestMeetingSignalsCommand`, `SubmitPeriodicScreenshotCommand`,
    `ApproveDeviceAuthorizationCommand`, `PollDeviceAuthorizationCommand`, `ApproveDeviceChangeRequestCommand`,
    `RejectDeviceChangeRequestCommand`, `ConfirmWorkLocationCommand`.
11. **Approvals and reasons**: `DecideWorkApprovalRequestCommand`, `ApproveLocationChangeRequestCommand`,
    `RejectLocationChangeRequestCommand`, `CreateLocationChangeRequestCommand`,
    `TraySubmitLocationChangeRequestCommand`, `AddClockingSessionReasonCommand`, `AddPercentageLogReasonCommand`,
    `MoveReportingLinesCommand`, `StartSprintCommand`, `CompleteSprintCommand`.
12. **The rest**: `CreateConfigurationTemplateCommand`, `UpdateConfigurationTemplateMetadataCommand`,
    `UpdateGlobalApplicationCommand`, `CreateIntegrationCommand`, `UpdateIntegrationCommand`,
    `LinkIntegrationModuleCommand`, `UnlinkIntegrationModuleCommand`, `SetIntegrationActivationCommand`,
    `CreateTrayReleaseCommand`, `UpdateTrayReleaseCommand`, `SetChatIconCommand`, `ExecuteInboxActionCommand`,
    `OpenConversationCommand`, `ReactToConversationMessageCommand`, `AddTaskCommentReactionCommand`,
    `RemoveTaskCommentReactionCommand`, `AddDashboardWidgetCommand`, `SaveDashboardLayoutCommand`.

### Outside the architecture test

The test only looks at commands that take strings. These commands take id lists from the request and still need a
maximum count:

- `AddCalendarEventParticipantsCommand`
- `PreviewReportingLineMoveCommand`
- `AddSeriesAttendeesCommand` (its id list; it is also listed above)
- every other command whose only input is a `Guid` list

Two Create/Update validator pairs share only two or three rules, so task 19 left them alone. When those commands are
next changed, consider moving the shared rules into a base validator the way task 19 did:

- `CreateRoleCommandValidator` and `UpdateRoleCommandValidator`
- `CreateChecklistTemplateCommandValidator` and `UpdateChecklistTemplateCommandValidator`

Paged queries can use `ValidPageRequest`. Some handlers clamp the page size instead of rejecting it, so decide per
query whether changing to a 400 is acceptable.

## Done when

- `CommandValidatorAllowList.Commands` is empty. The coverage test stays, so every new command that takes strings
  needs a validator from then on.
- Every new validator has unit tests.
- Each module's report lists the limits chosen and every input that now gets a 400.
