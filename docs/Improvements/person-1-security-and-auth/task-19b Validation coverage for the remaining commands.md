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

## Results (2026-11-19)

Branch: `claude/improve-task-19b-remaining-validators-f22c5c`, cut from the task 19 branch. Nothing has been pushed.

### Summary

- **The allow-list is empty.** `CommandValidatorAllowList.Commands` has no entries, and the coverage test stays.
  Every string-carrying command now has a validator.
- **110 command validators.** That is the 108 allow-listed commands, plus `AddCalendarEventParticipantsCommand`
  and `PreviewReportingLineMoveCommand`, which take only id lists.
  - 11 helper validators cover shared request types: base validators over small input interfaces, and validators
    for nested request records.
  - Create/Update pairs share a base validator, the way task 19 did it:
    - support tickets (admin and tenant)
    - announcements
    - legal documents
    - calendar events
    - the integration catalog
    - role templates
- **New shared rules** in `ONEVO.Application.Common.Validation`:
  - `InputLimits`: file name 255, content type 150, email 255, short key 100.
  - `ValidStringList(maxCount, maxItemLength)`: a list of short strings such as emails, codes, scopes or keys.
  - `ValidStringMap` and `ValidNullableStringMap`: credential maps, template variables and column mappings. Their
    messages never echo a value, so a secret is not sent back in the error.
- **Unit tests.** 65 test methods in `tests/ONEVO.Tests.Unit/Features/Validation/Remaining/`, with a shared
  `ValidationTestData` helper. Each validator is tested at its limits and just past them.
- **Commits, one per module:** support, legal, meetings and calendar, notifications, credentials and integrations,
  bulk onboarding and uploads, platform admin, then tray, location, approvals, chat and dashboard. A last fix raises
  the inbox review-note caps (see "Frontend impact").

Unless a row says otherwise, a limit is the EF column size. Missing values, known-value lists (providers, statuses,
scopes, channels, actions) and business rules stay in the handlers, which keep their friendlier messages.

### Limits chosen by module

| Module | Commands | Limits and reasons |
|---|---|---|
| DevPlatform support | ticket create/update (admin and tenant), status, comments (admin and tenant), attachments (admin and tenant), announcement create/update | Subject: rich text, 200. Description and comment: rich text, 10,000. Priority and status 20. Category 80. Attachment file name 255, content type 150. Announcement title 200 and body 5,000 (both rich text), severity 20, audience 30, window of at most 366 days. |
| Legal documents | create, update, publish | Document type 80, version 50, title 200, block scope 40. The content columns are nvarchar(max), so caps are chosen: text 250,000, HTML 500,000 (rich text), editor JSON 1,000,000. A long terms-of-service is about 60,000 characters of text. Publish reason 2,000. |
| Meetings | create, edit, move, cancel, add series/occurrence attendees, remove series/occurrence guest, respond, create link | Title, location and recurrence rule 500. Timezone 50. Platform 30. Description: rich text, 50,000 (it is stored as the task description, and 50,000 matches the task-comment cap). People and guest lists at most 1,000 each (`MeetingBulkInviteService.MaxIdsPerList`), each guest email at most 255. Response status 30, reason 2,000. Scopes at most 100 as keys. `CancelMeetingCommand` has no reason field; its scope is bounded as a key. |
| Personal calendar | create/update event, add participants, create event meeting, remove guest, respond, holiday settings, start/complete/update connection | Event fields use the task 19 `EditRecurringOccurrence` limits (title 200, description 10,000, location and link 500, colour 7, span of at most 366 days). Recurrence 20, recurrence rule 500. Participants at most 1,000. Guests at most 20 (`MaxGuestsPerEvent`). Provider 30, sync direction 20, response status 30, reason 2,000, country code 2. OAuth code 4,096, state 2,048. |
| Notifications | create template, save draft, send test, update/reset setting | Event key 100, name 150. In-app title, email subject and tray title 255. Action key 50, recipient rule 50. The bodies are nvarchar(max): 10,000. Email HTML 200,000, and CSS and text 50,000 each, as a length cap only, because templates may contain `<style>`. Channels at most 10. Test variables at most 100, each value at most 2,000. |
| Credentials and integrations | payment gateways (create, rotate, update metadata, verify), platform OAuth apps (configure, rotate, activate, validate), platform service keys (create, rotate, activate, update, verify), integration catalog (create, update, link, unlink, activate), user and tenant integration connections, GitHub OAuth start/complete, disconnect | Every key, name, URL and id uses its column size. Secrets are encrypted into nvarchar(max) columns, so caps are chosen: secrets 4,096, private keys and tokens 16,384 (a PEM key for 4096-bit RSA is about 3,300 characters; a Microsoft access token is a few thousand). Country lists at most 250 (codes 2, names 120). Scopes at most 100, each 200. Credential and field maps at most 20 entries. URLs 2,048. Integration description 2,000. |
| Bulk onboarding | preview mapping, validate batch, resolve issues, upload batch | Mapping at most 200 columns (header 255, field 100). Issue key 1,100 and new cell value 1,000, which is above every draft column. Row list at most 200 (`CsvBatchParser.MaxRows`). The inline department create uses name 100 and code 20; the position create uses name 100 and code 5. File name 255, employment type 30. |
| Uploads | upload file, task pending upload, objective asset, leave document, periodic screenshot, app logo, my documents | File name 255 and content type 150 (`file_records`). App logo content type 100. Purpose and document type at most 100 as keys. Document name 150, the handler's own limit. |
| Platform admin | invite manager, accept invite, platform role permissions, admin tenant role, apply/create/update role template, create/update configuration template, update global app, create/update tray release | Email and full name 255. Password 128 (the login bound). Invite token 512 (the MFA challenge bound). Platform permission codes at most 1,000, each 120. Tenant permission codes at most 1,000, each 50. Role name 100 (the column; the tenant `CreateRoleCommandValidator` uses 50), description 255. Module keys at most 100, each 100. Template key 100, type 50, name 150, description 500, industry tag 50. App name 200, category 50, publisher 200. Tray release: versions 32, channel and source 16, URL 2,048, SHA-256 64, publisher 200, notes 10,000. |
| Tray and monitoring | poll/approve device authorization, approve/reject device change, ingest meeting signals, confirm work location | Device code and fingerprint 512 (the other tray validators already use 512). User code 32 (it is 8 characters). Meeting signals at most 10,000 per batch, about a week of one-minute samples, because the tray uploads its offline backlog in one call. Process name 260. Location type 20. Latitude within ±90, longitude within ±180, accuracy not negative. Review comments 4,000. |
| Time and attendance | create, tray-submit, approve and reject location change | Reason 2,000. Review comment 4,000. Coordinates as above. |
| Work management and org | decide work approval, start/complete sprint, clocking-session and percentage-log reasons, task-comment reactions, move and preview reporting lines | Approval comment 4,000. Edited payload 1,000,000: it is the whole form as JSON, and a meeting form can carry a 50,000-character description and 1,000 guests. Sprint goal 2,000, span of at most 366 days, disposition at most 100. Reasons 2,000. Emoji 32. Reporting-line moves at most 1,000 people. |
| Chats, inbox and dashboard | open conversation, react to message, set chat icon, execute inbox action, add widget, save layout | Source and kind 40 (`subject_*` columns). Inbox action at most 100 as a key. Emoji 32. Icon 64 (`EntityIconRules.MaxLength`), colour 9. Widget key 100, breakpoint 20, layout at most 200 widgets. |

### Inputs that now get a 400

A validation failure returns 400 with the standard `Validation Error` problem body and per-field `errors`, before
the handler runs. As in task 19, lengths are measured on the raw value. A value that fits only after trimming now
gets a 400, for example a support subject or a document name padded with spaces.

**Already rejected, now with a different status or body.** These checks already existed in the handler. Now the
validator gives the 400 with per-field errors:

- Support ticket subject, description or comment over its limit, or containing active content.
- Announcement title or body over its limit or containing active content, or one that ends before it starts.
- Meeting title, location or recurrence rule over 500.
- Calendar event ending before it starts, or with more than 20 guests. The guest count is now taken before
  duplicates are removed.
- Chat reaction emoji over 32 characters.
- A document name over 150 characters.

**Used to be accepted, or to fail with a 500 on the database write.** These now get a 400.

- **Over a column limit.** Any field over its column size used to fail with a 500 on the write:
  - keys, names, URLs and ids in payment gateways, OAuth apps, service keys, the integration catalog and
    integration connections
  - role and template names
  - tray release fields
  - upload file names and content types
  - calendar event title, location, link and colour
  - holiday country code
  - location type
  - widget key and breakpoint
- **Active content.** Legal document HTML is now checked before the allow-list check. Meeting descriptions with
  active content used to be stored.
- **Over a cap chosen for an nvarchar(max) column.** These used to be accepted:
  - legal content and publish reason
  - meeting description, response reasons and calendar descriptions
  - notification bodies and HTML
  - secrets, tokens and private keys
  - approval payloads and comments, review comments and reasons
  - sprint goals
  - release notes
  - integration descriptions
- **Over a list or map cap.** These used to be accepted:
  - meeting people and guests (over 1,000) and calendar participants (over 1,000)
  - countries, scopes, permission codes, module keys and channels
  - credential maps, test variables and bulk mappings
  - bulk-onboarding row lists
  - meeting-signal batches (over 10,000) and dashboard layouts (over 200 widgets)
  - reporting-line moves (over 1,000 people)
- **Date spans:**
  - an announcement window over 366 days
  - a calendar event over 366 days
  - a sprint that ends before it starts or runs over 366 days
- **Coordinates** off the globe, or a negative accuracy, on a work-location confirmation or a location change
  request.
- **OAuth input.** A calendar or GitHub OAuth code over 4,096 or state over 2,048. The calendar callback already
  answered failures with a Problem 400.

### Frontend impact

I checked the Angular app's `maxlength` and `Validators.maxLength` values against these limits.

- **Fixed here.** The inbox approve and reject dialogs (`inbox-outcome-dialog`, `inbox-comment-dialog`) allow a
  4,000-character note. The inbox providers pass it straight into the location-change, device-change and
  work-approval decision commands. Those caps were first set to 2,000 and are now 4,000, so the dialogs keep
  working. The columns are nvarchar(max).
- **Still open, from task 19.** The same inbox dialogs also feed `ApproveAttendanceCorrectionCommand` and
  `RejectAttendanceCorrectionCommand` (review comment at most 2,000) and `RejectAccessGrantRequestCommand` (decision
  note at most 500, which is the nvarchar(500) column). A note longer than those now gets a 400 from the inbox. The
  access-grant limit is the real column size, so the fix belongs in the frontend: a per-action `maxlength`. The
  attendance correction cap could be raised to 4,000.
- **No other case found.** No other frontend limit is above the backend limit. Emails are 254 on the frontend and
  255 here, leave approval texts are 1,900, and the attendance correction modal is 2,000.

### Not done

- The commands named under "Outside the architecture test" have validators. I did not sweep every other command
  that takes only a `Guid` list.
- Paged queries were not changed to `ValidPageRequest`.
- The Role and ChecklistTemplate Create/Update pairs were not merged into a base validator. Those commands were not
  touched.

### Verification

See the final gate below.
