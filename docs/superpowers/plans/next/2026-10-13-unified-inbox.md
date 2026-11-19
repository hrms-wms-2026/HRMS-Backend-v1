# Unified Inbox: Router Engine + Conversations (group chat) — Implementation Plan

- **Date:** 2026-10-13
- **Repos:** `HRMS-Backend-v1` (BE) and `Hrms--Web-application---front-end---v1` (FE)
- **Branch (both repos):** `feature/unified-inbox`, cut from `development` (FE: `Development`)
- **Executor:** Claude Code. Use subagent-driven development, one plan part per subagent, with a review after each part.
- **Gate after every part:** BE `dotnet build` + full unit + architecture suites green. FE `ng build` + full `vitest` suite green (pre-existing flakes listed in the part report, not hidden).

---

## 0. Decisions (confirmed with the user, 2026-10-13)

| # | Decision |
|---|---|
| D1 | Requests/alerts become **user-oriented only**. A new **Inbox** item goes in the main left rail at **position 3** (Home, Work, **Inbox**, People, …). |
| D2 | Inbox shows **WM approval requests** (engine requests + module invitations) **and WM project-monitor alerts**. |
| D3 | The project-level **Approvals tab** (`/work/:id/approvals`) is **deleted fully**: route, sub-menu link, pending-count badge, page component, page store and specs. The Inbox **Project filter** replaces it. |
| D4 | A **Router engine** (`IInboxRouter`) sits between the Inbox API and the source engines. Each module plugs in one `IInboxSourceProvider`. WM is the only provider now. Leave, HR notifications and escalations plug in later with **no Inbox/Conversation code change**. |
| D5 | Every request and alert has a **conversation** (group chat). Base participants are the sender and receiver (for alerts: the alert's recipients). **Any participant can add members.** Added members **auto-join** at once, get a notification, and a system message "X added Y" appears in the chat. |
| D6 | Added members can **view the item + chat + tag + add others**. They **cannot** approve, reject, cancel or revert. Item actions stay with the source engine's own rules. |
| D7 | **@mention (tag)** in the chat composer. A tagged participant gets a push notification. A user can only tag current participants, and tagging a non-participant is offered as "add and tag", which adds them first (same as D5). |
| D8 | Chat is **live over SignalR** (new `/hubs/inbox` hub, `@microsoft/signalr` in FE). |
| D9 | The existing WM **approval comments** (threaded replies, edit) are **dropped, not migrated**: table, entity, repo, commands, endpoints, FE component and API methods are deleted. |
| D10 | Inbox + Conversations are **new shared modules** (`Features/Inbox`, `Features/Conversations`, FE `src/app/modules/inbox`). Shared files outside WM are touched **additively only**: `Program.cs` (hub + JWT query token), `DependencyInjection.cs`, `NotificationTemplateSeeder` (new rows), `nav-items.config.ts` + nav icon (one item), `notification-navigation.service.ts` (WM branch), `notification-bell` (WM decision calls), `package.json` (`@microsoft/signalr`). No other teammate module is changed. |
| D11 | The chat composer is the **usual composer** — `comment-composer` + `rich-text-editor` from WM, moved to `shared/ui` and extended with @mention. The slash (`/`) menu in the chat composer also lists the item's **available actions** (e.g. `/approve`, `/reject`, `/add`), which run through the router. This is the "command box". |
| D12 | All existing filters come across, plus a **Project** filter, **Source module** filter and **Category** (Request / Alert). Filters are generic (provider supplies options), so future sources add their own options. |

**Out of scope (v1):** removing members / leaving a conversation, editing or deleting chat messages, attachments in chat, email delivery, migrating old approval comments, moving Leave / Attendance alerts in (that comes later as new providers).

---

## 1. Architecture

```
FE /inbox ──HTTP──▶ InboxController ──▶ IInboxRouter ──▶ IInboxSourceProvider[] (DI, keyed by Source)
                                                         └─ WorkInboxSourceProvider (WM)
                                                              ├─ approval feed (user-scoped, all projects)
                                                              ├─ monitor alerts (caller is a recipient)
                                                              └─ actions → existing WM commands
FE chat panel ──HTTP──▶ ConversationsController ──▶ Conversations feature
                                                      └─ IConversationSubjectResolver (per Source; WM implements)
            ◀─SignalR── InboxHub (/hubs/inbox) ◀── IInboxRealtimePublisher (Api layer impl)
Notifications: INotificationDispatcher.SendTemplatedAsync (existing bell)
```

### 1.1 Item identity
Every item is addressed by an **ItemRef** = `(Source, Kind, Id)`:
- `work / approval / {WorkApprovalRequest.Id}`
- `work / invitation / {invitation id}` (same id the current feed uses with `Source=invitation`)
- `work / monitor_alert / {MonitorAlert.Id}`

Routes, conversations and realtime groups all key on ItemRef. Future: `leave / request / {id}`, `hr / escalation / {id}`.

### 1.2 Identity
WM and Conversations use **EmployeeId**. The bell's `Notification.RecipientUserId` is a **UserId**. Convert through the existing employee→user lookup used by `WorkNotificationOutboxHandler` (reuse it, do not write a new one).

---

## 2. Backend parts

### Part B1 — Conversations core (`Features/Conversations`)

**Domain** (`ONEVO.Domain/Features/Conversations/Entities`), all tenant-owned (`BaseEntity` + TenantId):
- `Conversation`: `Id, TenantId, SubjectSource, SubjectKind, SubjectId, CreatedAt, LastMessageAt`. Unique index `(tenant_id, subject_source, subject_kind, subject_id)`.
- `ConversationParticipant`: `Id, ConversationId, EmployeeId, JoinReason (base|added), AddedByEmployeeId?, AddedAt, LastReadAt?`. Unique `(conversation_id, employee_id)`.
- `ConversationMessage`: `Id, ConversationId, AuthorEmployeeId? (null = system), Kind (text|system), BodyHtml, SystemEventJson?, CreatedAt`. Index `(conversation_id, created_at)`.
- `ConversationMessageMention`: `MessageId, EmployeeId` (PK both).

**Infrastructure:** EF configurations (snake_case tables `conversations`, `conversation_participants`, `conversation_messages`, `conversation_message_mentions`), repositories, one migration `AddConversations`, plus an **RLS coverage migration** for the four tables (follow the `AddCalendarEventsRlsPolicyCoverage` recipe). The architecture suite must stay green.

**Application:**
- `IConversationSubjectResolver` (one per Source, resolved by `Source` key):
  - `Task<SubjectAccess?> ResolveAsync(ItemRef subject, Guid callerEmployeeId, ct)` returns `null` if the subject does not exist or is not visible to the caller **as a base participant**. Otherwise it returns `{ BaseParticipantEmployeeIds, Title, ProjectId? }`.
- `ConversationAccess` service: the caller can read/write if they are a **base participant** (from the resolver) **or** an **added participant** row. It fails closed.
- Commands / queries (MediatR, `Result<T>`, FluentValidation like the rest of the codebase):
  - `OpenConversation(ItemRef)`: get-or-create the conversation, then upsert base participants (`JoinReason=base`). It is idempotent and safe under concurrency (unique index + catch duplicate → re-read). Returns the conversation, participants (name, avatar, joinReason) and the first page of messages.
  - `ListConversationMessages(conversationId, before?, take=50)`
  - `PostConversationMessage(conversationId, bodyHtml, mentionEmployeeIds[])`: sanitize HTML with the existing sanitizer used by task comments. Mentions must be current participants, otherwise 422. Writes the message + mentions, bumps `LastMessageAt`, notifies the tagged employees (template `conversation_mentioned`), and publishes realtime `messageCreated`.
  - `AddConversationParticipants(conversationId, employeeIds[])`: any participant may call it. Employees must be active and in the same tenant. Already-present employees are skipped. For each new one: insert a participant row (`added`), a system message `{type:"participant_added", by, employee}`, notification `conversation_member_added`, and realtime `participantAdded` + `messageCreated`, plus `inboxChanged` to the added user.
  - `MarkConversationRead(conversationId)`: sets `LastReadAt = now`.
  - `GetUnreadCounts(ItemRef[])`: batched, used by the router to fill `UnreadMessages`.
- **Notification templates** (add rows to `NotificationTemplateSeeder`, additive only): `conversation_member_added` ("{{actorName}} added you to "{{itemTitle}}"") and `conversation_mentioned` ("{{actorName}} mentioned you in "{{itemTitle}}""). `RelatedEntityType = "inbox_item"`, and `RelatedEntityId = SubjectId`. Put the source and kind in a placeholder **or** encode the type as `inbox:{source}:{kind}`, picking whatever `NotificationItem` can already carry. Check `NotificationResponse` first and do not change the shared notification schema.

**Tests (unit):** access (base / added / stranger / cross-tenant), open is idempotent, post rejects a non-participant mention, post sanitizes, add skips existing members, add writes a system message + notification, unread count after read.

### Part B2 — Realtime (`/hubs/inbox`)

- `ONEVO.Api/Hubs/InboxHub.cs`: `[Authorize]` with the **normal tenant user JWT** (not the tray policy).
  - `OnConnectedAsync`: add the connection to group `inbox:user:{employeeId}`.
  - `JoinConversation(conversationId)`: checks `ConversationAccess`, then adds to `inbox:conv:{id}`. `LeaveConversation(id)`.
- `Program.cs`: `app.MapHub<InboxHub>("/hubs/inbox")`. In the JwtBearer options, add `OnMessageReceived` that reads `access_token` from the query string **only when the path starts with `/hubs/inbox`**. If a JwtBearer `Events` object already exists, compose with it — do not replace it.
- `IInboxRealtimePublisher` (Application interface) with an Api-layer implementation over `IHubContext<InboxHub>`, the same pattern as `SignalRTrayPolicyRefreshNotifier`:
  - `MessageCreated(conversationId, dto)`
  - `ParticipantAdded(conversationId, dto)`
  - `InboxChanged(employeeIds[], ItemRef)`
- Publish **after** the DB commit. A realtime failure is logged and swallowed, never failing the request.

**Tests:** publisher is invoked with the right groups (mocked hub context); `JoinConversation` denies a stranger.

### Part B3 — Inbox router engine (`Features/Inbox`)

**Contracts** (`Features/Inbox/Contracts`):
```csharp
public interface IInboxSourceProvider {
    string Source { get; }                                   // "work"
    Task<IReadOnlyList<InboxItem>> ListAsync(InboxQuery q, Guid callerEmployeeId, CancellationToken ct);
    Task<InboxItemDetail?> GetDetailAsync(string kind, Guid id, Guid callerEmployeeId, CancellationToken ct);
    Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid callerEmployeeId, CancellationToken ct);
    Task<Result<InboxActionResult>> ExecuteActionAsync(string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct);
}
```
- `InboxItem`: `Source, Kind, Id, Category (request|alert), ActionType, Title, Subtitle, Status, Direction (received|sent|info), Severity?, ContextType ("project"), ContextId, ContextName, RequestedBy {id,name}, RequestedTo {id,name}?, CreatedAt, UpdatedAt, AvailableActions[] {key,label,tone,needsComment,needsPayload}, UnreadMessages, IsPending`.
- `InboxItemDetail`: `Item` + `Steps[]` (`{key, label, actor, at, state: done|current|upcoming, note}`), `Summary`, `ChangeSet` (reuse WM `ApprovalChangeSetBuilder` output as a generic `{field, before, after}[]`), `Explanation` (alert message + details), `Target` link `{route: string[], label}`, and `EditablePayload?` (for WM "edit before approve").
- `InboxQuery`: `Category[], Source[], ContextIds[] (projects), Status[], Direction[], ActionTypes[], RequestedBy[], From?, To?, Search?, Page, PageSize`.
- `InboxFilterField`: `{ key, label, multi, options[{value,label}] }`. The router merges fields with the same key across providers.

**Router (`InboxRouter : IInboxRouter`):**
- `ListAsync`: fan out to the providers matching `Source[]` (all when empty) in parallel, merge, sort (`IsPending desc, UpdatedAt desc`), and page. Fill `UnreadMessages` with one batched `GetUnreadCounts` call.
- `GetDetail / ExecuteAction`: dispatch by `Source`. Unknown source → 404.
- `GetCounts`: `{ pendingReceived, unreadMessages, openAlerts }` for the rail badge.
- After a successful action, publish `InboxChanged` to the participants returned in `InboxActionResult.AffectedEmployeeIds`.

**API:** `InboxController` (`api/v1/inbox`):
- `GET /` (query = `InboxQuery`)
- `GET /filters`
- `GET /counts`
- `GET /{source}/{kind}/{id}`
- `POST /{source}/{kind}/{id}/actions/{action}`

**Conversations API:** `ConversationsController` (`api/v1/conversations`):
- `POST /open` (body ItemRef)
- `GET /{id}/messages?before=`
- `POST /{id}/messages`
- `POST /{id}/participants`
- `POST /{id}/read`

Add Postman docs under `docs/postman-request/Inbox/` and `docs/postman-request/Conversations/`, matching the existing docs format.

**Tests:** merge and sort order, paging across providers, unknown source 404, filters merged by key, counts.

### Part B4 — WM provider (`Features/WorkManagement/Inbox`)

- `WorkInboxSourceProvider : IInboxSourceProvider` (`Source="work"`).
  - **Approvals:** extract the body of `GetProjectApprovalFeedQueryHandler` into an `ApprovalFeedBuilder.BuildAsync(projectIds, caller)`. Keep `ApprovalFeedItemFactory` and `ApprovalFeedParticipants`. Add a user-scoped path over **all projects the caller can see** (same project-visibility rule the project list uses). Map `ApprovalFeedItemResponse` → `InboxItem` (`Category=request`, `Kind=approval|invitation`, `Direction`, `AvailableActions` from `CanDecide`/`CanCancel`/revert window).
  - **Monitor alerts:** open alerts, plus alerts resolved in the last 30 days, where the caller is a **recipient**. A recipient is the same person `ProjectMonitorService` notifies: the creator-position holder, resolved through the existing hierarchy service (reuse it, do not re-implement). Also include the `SubjectEmployeeId`. Map to `Category=alert`, `Kind=monitor_alert`, `Status=open|resolved`, `Severity` from `RuleCode`, with no decision actions, only `open_target`. Alerts on achieved modules keep the current rule: shown but `IsPending=false` and not counted.
  - **Filters:** Project (caller's projects), Type (WM action types + alert rule codes, using `WorkActionLabels`), Status, Direction, Requested by, Module.
  - **Actions:** `approve` (optional `editedPayloadJson` + comment), `reject` (comment required, as today), `cancel`, `revert`, `accept_invitation`, `decline_invitation`. Each maps to the **existing** MediatR command. Do not duplicate any rule.
  - **Detail Steps:** Requested → (Approved | Rejected | Cancelled | Stale) → (Reverted). For alerts: Detected → Notified → Resolved.
- `WorkConversationSubjectResolver : IConversationSubjectResolver` (`Source="work"`) gives the base participants:
  - approval: requester + current eligible approver(s) (the position holder resolved at read time, so a transfer moves the chat to the new holder);
  - invitation: inviter + invitee;
  - alert: the recipients above.
- **Realtime hooks:** after WM approval create / decide / cancel / revert, after an invitation decision, and after the monitor creates or resolves an alert, call `IInboxRealtimePublisher.InboxChanged(affected)`. Do it in the existing WM notification engine path, one call site, not per handler.

**Tests:** user-scoped feed across 2 projects, caller in project A only → no B items, alert recipient rule, transfer moves the base participant, each action maps to the right command, a member added to the conversation **cannot** approve (403/422 from the WM command, unchanged).

### Part B5 — Remove old approval comments + project feed endpoint

- Delete:
  - `WorkApprovalComment` entity + configuration + `IWorkApprovalCommentRepository`/`EfWorkApprovalCommentRepository`;
  - `Approvals/Comments/**`;
  - `ApprovalCommentResponse`;
  - the `approval-comments` endpoints in `WorkApprovalsController`;
  - `CommentCount` from `ApprovalFeedItemResponse` (replaced by `UnreadMessages` on InboxItem);
  - the comment hooks in `CreateTaskCommentCommandHandler` / anything referencing `ApprovalCommentPosting` (grep first).
- Migration `DropWorkApprovalComments`.
- Delete the `projects/{projectId}/approval-feed` endpoint and `GetProjectApprovalFeedQuery`, **only if** grep shows no FE caller after F5. **Keep** `approvals/{id}/approve|reject|cancel|revert` and the detail endpoint: the dashboard `action-center-decision.service` (teammate's module) and the bell still call them.
- Remove tests of the deleted code. List every removed test in the commit message.
- **Check for snapshot corruption** after adding migrations: `ApplicationDbContextModelSnapshot.cs` must only differ by this branch's entities. A stale checkout corrupts it.

### Part B6 — Backend test-coverage audit + gate

Per-handler checklist: every new command, query, provider method, resolver, router method and hub method has at least one happy-path and one denial/edge test. Write the missing ones. Run the full unit + architecture suites. Report the counts.

---

## 3. Frontend parts

### Part F1 — Shared building blocks

1. Move `modules/work/ui/rich-text-editor` and `modules/work/ui/comment-composer` → `shared/ui/` and update every WM import. The composer's attachment upload stays optional via an input (chat passes `attachments=false`).
2. Move `modules/work/ui/task-filter-bar` → `shared/ui/filter-bar` (generic name `app-filter-bar`). Update the WM imports. Behaviour is unchanged.
3. **@mention** in `rich-text-editor`: typing `@` opens a suggestion popover (the same positioning code as the slash menu). Input `mentionCandidates: {id,name,avatarUrl}[]` and `allowAddAndTag: boolean`. A mention is inserted as `<span class="mention" data-employee-id="…">@Name</span>`. Output `mentionsChange: string[]`. Picking a non-participant (from the employee search) emits `addAndTag(employeeId)`.
4. **Slash commands from host:** input `extraSlashCommands: {id,label,category,icon}[]`, output `slashCommand(id)`. The chat uses it for item actions.
5. `core/realtime/inbox-realtime.service.ts`: `@microsoft/signalr` (pin an exact version), `accessTokenFactory` from `AuthStore`, automatic reconnect, `joinConversation/leaveConversation`, and signals/observables `messageCreated$`, `participantAdded$`, `inboxChanged$`. Connect only when `tenantWorkspaceReady(...)` is true (same gate as the bell).

Specs for each moved/extended component and the realtime service (mock `HubConnectionBuilder`).

### Part F2 — Inbox data-access, store, nav

- `modules/inbox/models/inbox.dto.ts` (mirrors B3), `data-access/inbox-api.service.ts`, `data-access/conversations-api.service.ts`.
- `state/inbox.store.ts` (signals): `items`, `filters`, `filterFields`, `selectedRef`, `detail`, `counts`, loading/error, `applyFilter`, `select`, `runAction`. It listens to `inboxChanged$` → refresh the list + counts (debounced 500 ms).
- `state/conversation.store.ts`: `open(ref)`, `messages`, `participants`, `loadOlder`, `send(html, mentions)`, `addParticipants(ids)`, `markRead`. It appends from `messageCreated$`, deduplicated by id.
- `inbox.routes.ts`: `''` → `InboxPageComponent`; `':source/:kind/:id'` → same component with that item selected (deep link). Register in `app.routes.ts` under the main layout.
- `nav-items.config.ts`: insert `{ id: 'inbox', label: 'Inbox', icon: 'inbox', path: '/inbox' }` at **index 2**. Add `'inbox'` to `NavIconName` + the rail icon SVG (tray/inbox glyph). Rail badge = `counts.pendingReceived + counts.unreadMessages` (cap `9+`). Update `nav-access.spec.ts`.

### Part F3 — Inbox page

Layout follows today's Approvals page (it is the same alert type, just user-scoped):
- **Toolbar:** tabs `All · Requests · Alerts`, segment `Received · Sent`, `app-filter-bar` with fields from `/inbox/filters` (Project, Source, Type, Status, Requested by, Module, Requested on), and search.
- **List:** `app-data-table` with columns Title (type icon + unread-chat dot + subtitle), Project, Type pill, Requested by, Requested to, Status pill, kebab (item `AvailableActions`). Empty states for "nothing yet" vs "no match".
- **Detail card** (right side, same slide-in as today): `inbox-item-card` = header (title, status, project, target link) → **Request steps** timeline (from `Steps`) → **explanation** (summary + change-set table, or alert message/details) → **action bar** (Approve / Reject with comment dialog / Cancel / Revert / Accept / Decline, plus the existing **edit-before-approve** form reused from `approval-explanation-card`) → **conversation panel** (F4).
- Move the reusable parts of `approval-explanation-card` (change-set rendering, edit-before-approve form, type icon) into `modules/inbox/ui/`. The rest is deleted in F5.
- Selecting a row updates the URL to `/inbox/:source/:kind/:id`. Back/forward work.
- Mobile: the card becomes full-screen with a back button.

### Part F4 — Conversation panel

`modules/inbox/ui/conversation-panel`:
- **Header:** participant avatar stack (base vs added tooltip) + **Add member** button → `employee-picker` (multi) → `addParticipants`.
- **Message list:** grouped by day, own messages right-aligned, system messages centred ("Kajan added Priya · 10:42"), mention spans highlighted (the current user's own mention stronger), "load older" on scroll-top, auto-scroll on new messages only when already at the bottom, and an unread divider.
- **Composer:** shared `comment-composer` (`collapsible=false`, `submitLabel="Send"`, Enter = send, Shift+Enter = newline). Mentions use the current participants, and add-and-tag runs `addParticipants`, then inserts the mention. The slash menu lists the item's `AvailableActions` (+ `/add`), and choosing one calls the same handler as the action bar.
- `joinConversation` on open, `leaveConversation` on destroy/switch, and `markRead` on open and when new messages arrive while visible.

### Part F5 — Remove the old WM Approvals tab + repoint entry points

- Delete:
  - `modules/work/feature/work-approvals/**`;
  - the `approvals` child route in `work.routes.ts`;
  - the Approvals link + pending badge in `project-detail` (and its `WorkApprovalsStore` injection);
  - `modules/work/state/work-approvals.store.ts` (if nothing else uses it after this);
  - `modules/work/ui/approval-comments/**`;
  - what's left of `approval-explanation-card`;
  - comment + feed methods/DTOs in `work-approvals-api.service.ts`.
- **Keep** the decision methods (`approveWorkApproval`, `rejectWorkApproval`, `accept/rejectObjectiveInvitation`, …), because the dashboard and the bell use them.
- `notification-navigation.service.ts`: WM approval/invitation notifications go to `/inbox/work/{approval|invitation}/{id}`, and monitor-alert notifications go to `/inbox/work/monitor_alert/{id}`. `inbox_item` (conversation mention/added) notifications go to the item.
- `tree-alert-mark` / tree explanation panel: add an "Open in Inbox" link to `/inbox/work/monitor_alert/{id}`. The Tree red marks stay.
- Grep for `approvals'`, `approval-feed`, `approval-comments`, `WorkApprovalsStore` → zero hits outside kept code. Remove the dead specs and list them in the commit message.

### Part F6 — Frontend test-coverage audit + gate + browser pass

- Per-component/store checklist (inbox store, conversation store, page, card, steps, conversation panel, mention editor, realtime service, nav badge, navigation service): write the missing specs.
- `ng build` + full suite.
- **Browser pass** (two users in two browser sessions):
  1. A member requests a task edit → the owner sees it in the Inbox live.
  2. Filter by Project.
  3. Open the item → steps + change set.
  4. Chat both ways live.
  5. The owner adds a third user → the system message appears, the third user gets a bell notification and can chat but sees no Approve button.
  6. Tag → bell notification.
  7. `/approve` from the slash menu works.
  8. A monitor alert appears under Alerts.
  9. The old `/work/:id/approvals` URL is gone.
  10. Rail badge counts.

---

## 4. Order & dependencies

`B1 → B2 → B3 → B4 → B5 → B6` and `F1 → F2 → F3 → F4 → F5 → F6`. F2+ needs B3/B4 running locally. F4 needs B1/B2. The BE and FE tracks can run in parallel up to F2.

## 5. Risks

- **JWT on the hub:** browsers can't set headers on WebSockets, so the query-string token must be limited to `/hubs/inbox`.
- **Position-based approver:** chat base participants must be resolved at read time, or a transfer leaves the old holder in the chat as a base member.
- **Cross-module imports:** the inbox imports nothing from `modules/work`. Shared pieces go through `shared/ui` or `core`.
- **Migration snapshot:** verify `ApplicationDbContextModelSnapshot.cs` after each migration.
- **Teammate code:** additive edits only in the shared files listed in D10.
