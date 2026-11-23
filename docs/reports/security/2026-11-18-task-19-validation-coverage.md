# Task 19: validation coverage and shared rules (2026-11-18)

Branch: `claude/improve-task-19-validation-e305c2`, cut from `origin/development`. Nothing has been pushed.
The frontend is unchanged: its validation helps the user, and this server-side validation is a security bound.

## Summary

- **Shared rules.** New file `ONEVO.Application/Common/Validation/ValidationRuleExtensions.cs` adds `ValidName`,
  `ValidRichText`, `ValidIdList`, `ValidDateRange` (with a `DateTimeOffset` and a `DateOnly` overload) and
  `ValidPageRequest`. `ValidRichText` applies `RichTextRules.HasActiveContent`.
  - All of the new validators use these rules.
  - `ValidPageRequest` isn't used yet, because none of the 30 commands is paged. It is unit-tested and ready for
    the follow-up.
- **30 validators**, each with unit tests. There are 102 tests in `tests/ONEVO.Tests.Unit/Features/Validation/`.
- **Create/Update duplicates removed** for these pairs:
  - clock-in policy
  - position
  - department
  - leave type
  - work mode

  Each pair now derives from one abstract base validator over a small input interface that both commands
  implement. The base is abstract, so `AddValidatorsFromAssembly` doesn't register it a second time. Rules and
  messages are unchanged, and `SharedCreateUpdateValidatorTests` pins them.
- **Architecture test.** New file `tests/ONEVO.Tests.Architecture/Validation/CommandValidatorCoverageArchitectureTests.cs`.
  - It fails when a command that carries a string has no validator. A string can sit directly on the command, in
    a collection, or in a nested request type.
  - The 108 commands still to do are in `CommandValidatorAllowList.cs`.
  - The list only shrinks. The test also fails when a listed command gains a validator, stops taking strings, or
    no longer exists.
  - A theory pins the 30 commands from this task so their validators can't be deleted unnoticed.
- **Follow-up:** `docs/Improvements/person-1-security-and-auth/task-19b Validation coverage for the remaining commands.md`.

## Re-checked starting list

The scan we were given listed 281 commands. It matched validators by type name, so it double-counted. Re-checked by
reflection over the real types:

- 134 commands that carry a string had no validator.
- 26 of them are among the 30 done here.
- The other 4 of the 30 take only id lists: `ForwardMessageCommand`, `AddConversationParticipantsCommand`,
  `RequestBulkOnboardingFinalizeCommand` and `SetSprintTasksCommand`.
- That leaves 108 on the allow-list.

Several handlers in this group already check rich text, lengths or counts inline: `ConversationRules.ValidateBody`,
the task-comment handlers, `InboxChatHandlers` and `AttendanceCorrectionWorkflow`. Their validators repeat the same
limits so the bound holds before any database or lookup work. The handlers' required-field and business checks
are unchanged.

## The 30 commands and the limits chosen

Unless a row says otherwise, a limit is the EF column size from `src/ONEVO.Infrastructure/Persistence/Configurations`.

### User text and rich text

| Command | Limits | Why |
|---|---|---|
| `PostConversationMessageCommand` | `BodyHtml`: rich text, 10,000. `MentionEmployeeIds` ≤ 250. `AttachmentFileIds` ≤ 5. `Tag` ≤ 20. | `conversation_messages.body_html` is nvarchar(10000) (`ConversationRules.MaxBodyLength`). The attachment cap is the handler's `MaxAttachments`. `tag` is nvarchar(20). 250 people is the cap for one request. An empty body is left to the handler, which allows it when files are attached. |
| `EditConversationMessageCommand` | `BodyHtml`: rich text, 10,000. Mentions ≤ 250. | Same columns. |
| `CreateGroupChatCommand` | `Name` ≤ 80. `EmployeeIds` ≤ 250. | The group name is stored in system-event JSON, and `PeopleChatNames.MaxGroupName` is 80. "Name required" stays in the handler (422). |
| `RenameGroupChatCommand` | `Name` ≤ 80. | Same. |
| `SetChatDescriptionCommand` | `Description` ≤ 500. | `conversations.description` is nvarchar(500). |
| `StartCaseGroupCommand` | `Source` and `Kind` required, ≤ 40 each. `EmployeeIds` ≤ 250. `Name` ≤ 80. | `conversations.subject_source` and `subject_kind` are nvarchar(40). |
| `CreateTaskCommentCommand` | `Content`: rich text, 50,000. `AttachmentFileIds` ≤ 20. | `task_comments.content` is nvarchar(max), so the cap is chosen: 5× a chat message, which leaves room for formatted HTML and image tags (images are file references, not data URLs). |
| `EditTaskCommentCommand` | Same as create. | |

### Money

| Command | Limits | Why |
|---|---|---|
| `ProcessStripeEventCommand` | `EventType` required, ≤ 100. Subscription and customer refs ≤ 200. Status ≤ 50. `ExternalInvoiceId` ≤ 100. `InvoiceNumber` ≤ 50. `GatewayProvider` ≤ 50. `Currency` is 3 letters. Each amount fits `decimal(12,2)` (\|x\| ≤ 9,999,999,999.99, at most 2 decimal places). Period end ≥ start, span ≤ 3 years + 7 days. | Values come from the `tenant_subscriptions` and `subscription_invoices` columns. Amounts can be negative because of Stripe credits. Three years is Stripe's longest billing interval. |

### Dates and date ranges

| Command | Limits | Why |
|---|---|---|
| `RequestAttendanceCorrectionCommand` | `CorrectionType` required, ≤ 30. `Reason` required, ≤ 255. `Notes` ≤ 2,000. Clock-in to clock-out: end ≥ start, ≤ 25 h. At most 20 breaks; each break type ≤ 30, end ≥ start, ≤ 25 h. | `attendance_corrections`: correction_type is nvarchar(30) and reason is nvarchar(255). notes is nvarchar(max); 2,000 is the workflow's own limit. A break type is nvarchar(30) in the read model. All times fall on one local work date, and a daylight-saving day has 25 hours. |
| `PreviewAttendanceCorrectionCommand` | Same as request. | Same input. |
| `CreateApproverBackdatedLeaveCommand` | `Reason` ≤ 2,000. `WorkDate` within the last 366 days. | `leave_requests.reason` is nvarchar(max); 2,000 matches `SubmitLeaveRequestCommandValidator`. The handler only checks that the date is in the past. The new bound limits how far back an approver can backdate. |
| `EditRecurringOccurrenceCommand` | `Title` required, ≤ 200. `Description` ≤ 10,000. `Location` and `MeetingLink` ≤ 500. `Color` ≤ 7. End ≥ start, span ≤ 366 days. | `personal_calendar_events` columns. The description is nvarchar(max) and plain text: no calendar view in the frontend renders it with `innerHTML`. Its cap is generous because `CreateCalendarEventCommand` doesn't have a limit yet. |

### Review and decision text

| Command | Limits | Why |
|---|---|---|
| `ApproveAttendanceCorrectionCommand` | `ReviewComment` ≤ 2,000. Edited times and breaks follow the request rules. | review_comment is nvarchar(max); 2,000 matches notes. |
| `RejectAttendanceCorrectionCommand` | `ReviewComment` ≤ 2,000. | Same. |
| `CreateBypassRequestCommand` | `BypassReason` required, ≤ 500. `PenaltyDescription` ≤ 500. | `offboarding_task_bypass_requests` columns. |
| `RejectBypassRequestCommand` | `DecisionComment` ≤ 500. | decision_comment is nvarchar(500). |
| `RejectAccessGrantRequestCommand` | `DecisionNote` ≤ 500. | `access_grant_requests.decision_note` is nvarchar(500). |

### Id lists from the request

| Command | Limits | Why |
|---|---|---|
| `ForwardCaseCommand` | `Source` and `Kind` required, ≤ 40. `ConversationIds` ≤ 20. | Subject columns. 20 is the cap for one forward. |
| `ForwardMessageCommand` | `ConversationIds` ≤ 20. | Same. |
| `AddConversationParticipantsCommand` | `EmployeeIds` ≤ 250. | Same people cap as group chats. |
| `RequestBulkOnboardingFinalizeCommand` | `OnboardingDraftIds` ≤ 200. | A batch holds at most `CsvBatchParser.MaxRows` (200) rows. |
| `SetSprintTasksCommand` | `AddTaskIds` and `RemoveTaskIds` ≤ 500 each. | Abuse bound. Sprint planning moves far fewer tasks at once. |
| `AddCalendarEventGuestsCommand` | `Emails` ≤ 20, each ≤ 255. | `CalendarEventValidation.MaxGuestsPerEvent` is 20. `calendar_event_guests.email` is nvarchar(255). |

The id-list rule caps the count only. Handlers already drop empty and repeated ids, so the validators don't reject
them.

### Auth inputs

| Command | Limits | Why |
|---|---|---|
| `VerifyMfaCommand` | `Code` is exactly 6 digits. Challenge required, ≤ 512. `IpAddress` ≤ 45. `UserAgent` ≤ 500. | `OtpNetTotpService` only accepts 6 digits. The raw challenge is 88 characters (64 bytes in base64). IP and user agent are copied into `sessions` (nvarchar(45) and nvarchar(500)). |
| `VerifyAdminMfaCommand` | `Code` is 6 digits. Challenge ≤ 512. | Same. |
| `ConfirmAdminMfaSetupCommand` | `Code` is 6 digits. | Same. |
| `RequestPasswordResetCommand` | `Email` required, ≤ 255, email format. | Email columns are nvarchar(255). Only the format is checked, never whether an account exists, so known and unknown addresses still get the same answer. |
| `BaseForgotPasswordCommand` | Same. | Same. |
| `SubmitLegalAcceptanceCommand` | At most 20 items. `DocumentType` ≤ 80, `Version` ≤ 50, `Decision` ≤ 20, `ContentHash` ≤ 128, `IpAddress` ≤ 45, `UserAgent` ≤ 500. | `legal_acceptance_records` and `legal_document_versions.content_hash` columns. There are only a handful of required documents. An empty list and unknown decisions are still answered by the handler. |

`CancelMeetingCommand` was named as a risk ("meeting cancel reasons"), but it has no reason field, and
`MeetingCancelService` already checks its `Scope`. It stays on the allow-list. The two CoreHr reject commands took
its slot, along with the recurring-occurrence cancel that has no string input.

## Behaviour changes: inputs that now get a 400

A validation failure returns 400 with the standard `Validation Error` problem body and per-field `errors`, raised by
`ValidationBehavior` before the handler runs.

**Already rejected, now with a different status or body.** Where the old status was 400, only the body changes:
it now has per-field `errors` instead of the handler's single message. Where it was 422, the status changes to
400.

- Chat message body over 10,000 characters, or containing active content: the handler returned 400 with a plain
  message.
- More than 5 attachments on a message: the handler returned 422.
- Group or case-group name over 80 characters, or chat description over 500: the handler returned 422.
- Task comment with active content: the handler returned 400.
- Attendance correction reason missing or over 255, or notes over 2,000: the workflow returned 400.
- Calendar guest email over 255: the handler returned 400.
- Recurring occurrence ending before it starts: the handler returned 400.

**Used to be accepted or to fail with a 500.** These now get a 400.

- Task comment content over 50,000 characters, or more than 20 attachment ids. These used to be accepted.
- More than 250 mentions, participants added, group members or case-group members in one request. Used to be
  accepted.
- A forward to more than 20 chats. Used to be accepted.
- Message tag over 20 characters. The handler returned 422 for any tag other than `justification`.
- Attendance corrections:
  - more than 20 breaks, or a break type over 30 (both used to be accepted)
  - clock-in to clock-out longer than 25 hours (the workflow rejected this as "not on the work date")
  - review comment over 2,000 on approve or reject (used to be accepted; the column is nvarchar(max))
  - edited times on approve where the end is before the start
- Backdated leave for a day more than 366 days ago, or with a reason over 2,000. Used to be accepted.
  - This also applies when an attendance-alert inbox action converts an old alert into leave.
- Recurring occurrence edits:
  - a blank title (used to fail with a 500 on `Title.Trim()`)
  - a title over 200, a location or meeting link over 500, or a colour over 7 (used to fail with a 500 on the
    database write)
  - a description over 10,000, or a span over 366 days (used to be accepted)
- Stripe webhook:
  - an amount that doesn't fit `decimal(12,2)` or has more than 2 decimal places
  - a currency that isn't 3 letters
  - an id or number longer than its column
  - a period longer than 3 years + 7 days, or one that ends before it starts

  These used to be stored, or to fail with a 500 on the write. Stripe now gets a 400 and retries the event, so a
  bad event shows in the Stripe dashboard instead of failing silently.
- `SetSprintTasks` lists over 500, or a bulk-onboarding finalize request with more than 200 drafts. Used to be
  accepted.
- Offboarding bypass request:
  - an empty or blank `BypassReason` used to be stored as an empty string
  - a reason or penalty description over 500 used to fail with a 500
- Bypass rejection comment, or access-grant rejection note, over 500. Used to fail with a 500.
- MFA (tenant login, admin login and admin enrolment):
  - a code that isn't exactly 6 digits gets a 400 instead of a 401 "invalid code"
  - it no longer counts as a failed attempt on the challenge, because a malformed code can never verify;
    `AuthRateLimitingMiddleware` still limits the endpoint
  - a challenge cookie over 512 characters gets a 400
- MFA verify or legal acceptance with an IP over 45 characters or a user agent over 500. Used to fail with a 500
  on the database write.
- Password reset (tenant host and base domain):
  - a missing email used to throw a `NullReferenceException` (500)
  - a malformed email, or one over 255 characters, used to get the generic 200
- Legal acceptance with more than 20 items, or with an item field longer than its column.
- `StartCaseGroup` or `ForwardCase` with a blank `Source` or `Kind`. The handler used to answer "not found".

**Whitespace padding.** Lengths are measured on the raw value. Some handlers trim before checking, so a value that
fits the limit only after trimming now gets a 400. Measuring the raw value keeps the bound safe for handlers that
store the value untrimmed.

## Verification

Gate results are below. Every run used the worktree's full path, not the `backend` alias.

- **Full gate.** `verify.ps1 <worktree> -Full` (report folder `verify-20261118-134930`) passed every step:

  | Step | Result |
  |---|---|
  | build | pass |
  | unit | 8,121 passed |
  | architecture | 747 passed (was 713 on `origin/development`: 4 coverage tests plus 30 pinned high-risk commands) |
  | integration | 1,251 passed, 1 skipped |
- **Semgrep parse errors fixed.** The first security scan reported 3 Semgrep parse errors in this branch's files:
  Semgrep can't parse C# 12's body-less `class X : Base<T>;` form, so those files went unscanned. Four validators
  now have `{ }` bodies. After that change:
  - `verify.ps1 <worktree>` (report folder `verify-20261118-144906`) passed: build, 8,121 unit tests, 747
    architecture tests.
  - The security scan with `-Baseline origin/development` (report folder `security-20261118-145158`) showed
    Semgrep with 0 new findings and no parse errors.
- **Pre-existing scan findings.** The gitleaks (git history), DevSkim, NuGet and workflow findings were the same
  counts on both scans. None of them is in a file this branch changes.
- **Endpoint check.** `find-unauthorized-endpoints.ps1 <worktree>` found 0 actions without an authorization
  attribute. This branch adds no endpoints.
- **Allow-list guard checked by hand.** Removing a command from the allow-list failed
  `EveryCommandWithAStringProperty_HasAValidator_OrIsAllowListed`. Adding a validator for a listed command failed
  `AllowList_OnlyShrinks_EntriesThatGainedAValidatorMustBeRemoved`. Both probes were reverted afterwards.
