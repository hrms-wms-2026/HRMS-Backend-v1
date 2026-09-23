# Part 3: Project Member notifications, routed through the Outbox

**Read first:** `docs/superpowers/specs/next/2026-08-20-work-management-project-page-redesign-design.md`
§3.4. **Depends on Part 2 being done** — this part wires a notification call into the
`AddProjectMemberCommandHandler` Part 2 creates. Do Part 2 first.

**Scope guard:** Work Management module only (plus one shared/generic Outbox-handler addition under
`SharedPlatform`, which is the same kind of generic-but-WM-consumed addition the original Notification
Foundation work made — not a violation of the module boundary, same precedent as
`notification_templates`/`notifications`).

**Status:** done (backend)

## Goal

Two new notifications, both dispatched via the **Outbox** (not the synchronous
`INotificationDispatcher.SendTemplatedAsync` call the other 9 existing notification sites use — that's
deliberate, see the spec §3.4, and out of scope to change here):

- `work_project_member_invited` — sent to the invited employee, always, from
  `AddProjectMemberCommandHandler` (Part 2).
- `work_project_member_accepted` — sent to the inviter, **only** when the accepted invitation's
  Objective `IsDefault == true`, from the existing `AcceptObjectiveInvitationCommandHandler`.

## Why Outbox here and not the direct call the other 9 sites use

`INotificationDispatcher.SendTemplatedAsync` writes the `Notification` row in the same request/
transaction as the business change — simple, but ties notification latency/failure to the caller's
request. The Outbox decouples it: `IOutboxWriter.EnqueueAsync` just adds an `OutboxMessage` row in the
same transaction (cheap, can't fail independently), and the actual notification write happens later,
out-of-band, via the existing `OutboxProcessor` background poller (polls every `Outbox:PollSeconds`,
default 10s) calling a new `IOutboxMessageHandler`. This was the explicitly deferred design from the
original Notification Foundation work — "reuse the existing Outbox mechanism later" — and is now.

## Files to create

- `src/ONEVO.Application/Features/WorkManagement/Common/OutboxHandlers/WorkNotificationOutboxHandler.cs`
  (payload record + handler class, same file — matches the existing one-file-per-handler convention seen
  in `PasswordResetEmailOutboxHandler.cs`)
- `tests/ONEVO.Tests.Unit/Features/WorkManagement/WorkNotificationOutboxHandlerTests.cs`

## Files to modify

- `src/ONEVO.Application/Common/ServiceInterfaces/IOutboxMessageHandler.cs` — add
  `public const string WorkNotification = "work_notification";` to `OutboxMessageTypes`.
- `src/ONEVO.Application/DependencyInjection.cs` — register
  `services.AddScoped<IOutboxMessageHandler, WorkNotificationOutboxHandler>();` alongside the other
  `IOutboxMessageHandler` registrations (add a `using` for the new namespace).
- `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` — append the two new
  template entries to the `templates` list.
- `src/ONEVO.Application/Features/WorkManagement/Projects/Commands/AddProjectMember/AddProjectMemberCommandHandler.cs`
  (from Part 2) — enqueue the invited notification.
- `src/ONEVO.Application/Features/WorkManagement/ProjectInvitations/Commands/AcceptObjectiveInvitation/AcceptObjectiveInvitationCommandHandler.cs`
  — enqueue the accepted notification, gated on `objective.IsDefault`.

## Before writing code

Read in full: `IOutboxWriter.cs` (already simple, shown in the spec), `PasswordResetEmailOutboxHandler.cs`
(the payload-record + handler-in-one-file convention to mirror), `OutboxProcessor.ProcessBatchAsync`
(confirms handlers are resolved by `Type` string — case-sensitive, `StringComparer.Ordinal`), and
`RequestAllocationExtensionCommandHandler.cs` in full (this is your reference for **both** the
`ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync` display-name pattern AND the
`ExecuteInTransactionAsync` placement — notification/outbox calls happen *inside* the transaction,
right before `SaveChangesAsync`, never after). Also read `AcceptObjectiveInvitationCommandHandler.cs` in
full before editing it — confirm its current transaction structure and exactly where the membership
upsert happens, so you insert the notification call in the equivalent spot, not guessed blind.

## Tasks (small, do in order, one commit per task)

1. **`OutboxMessageTypes.WorkNotification`**: add the constant. No test needed for a constant alone.

2. **`WorkNotificationOutboxHandler`**: define
   `public sealed record WorkNotificationPayload(Guid TenantId, Guid RecipientUserId, string TemplateCode,
   Dictionary<string,string> Placeholders, string? RelatedEntityType, Guid? RelatedEntityId);` and
   `public sealed class WorkNotificationOutboxHandler : IOutboxMessageHandler` — constructor-injects
   `INotificationDispatcher`, `Type => OutboxMessageTypes.WorkNotification`, `HandleAsync` deserializes
   the payload (`JsonSerializer.Deserialize<WorkNotificationPayload>`, throw
   `InvalidOperationException` if null — mirror `PasswordResetEmailOutboxHandler`'s exact null-check
   phrasing) and calls `_notifications.SendTemplatedAsync(payload.TenantId, payload.RecipientUserId,
   payload.TemplateCode, payload.Placeholders, payload.RelatedEntityType, payload.RelatedEntityId, ct)`.
   - Test: happy path — valid payload JSON → `SendTemplatedAsync` called once with the exact deserialized
     values (mock `INotificationDispatcher`, assert the call). Null/malformed payload → throws.
     Idempotency note in the interface doc comment ("must be safe to retry") — confirm calling
     `HandleAsync` twice with the same payload is harmless (it is, since `SendTemplatedAsync` itself is a
     plain insert with no dedup — that's an accepted, pre-existing property of the underlying dispatcher,
     not something this handler needs to add dedup for).

3. **DI registration**: add the `using` + the `AddScoped<IOutboxMessageHandler, WorkNotificationOutboxHandler>()`
   line. No test — covered implicitly by any integration test that boots DI, if one exists; otherwise
   this is verified by the build succeeding + the handler tests above.

4. **Template seeder**: append
   ```csharp
   new()
   {
       Id = Guid.NewGuid(), Code = "work_project_member_invited",
       InAppTitleTemplate = "You've been added to a project",
       InAppBodyTemplate = "{{inviterName}} invited you to join {{projectName}}."
   },
   new()
   {
       Id = Guid.NewGuid(), Code = "work_project_member_accepted",
       InAppTitleTemplate = "Invitation accepted",
       InAppBodyTemplate = "{{accepterName}} accepted your invitation to join {{projectName}}."
   }
   ```
   to the `templates` list. The seeder is idempotent-by-code already (`GetTemplateByCodeAsync` check) —
   no test change needed here beyond whatever existing test (if any) asserts the full seeded-template
   count/list; update that count if it exists.

5. **Wire `AddProjectMemberCommandHandler` (Part 2)**: after the invitation is created but before
   `SaveChangesAsync`, resolve the inviter's display name via `ResolveDisplayNamesByEmployeeIdAsync`
   (same call shape as `RequestAllocationExtensionCommandHandler`), then
   `await _outboxWriter.EnqueueAsync(OutboxMessageTypes.WorkNotification, new WorkNotificationPayload(
   tenantId, assignee.UserId, "work_project_member_invited",
   new Dictionary<string,string> { ["inviterName"] = inviterDisplayName, ["projectName"] = project.Name },
   "project_member_invitation", invitation.Id), tenantId, ct);` — inject `IOutboxWriter` into the
   handler's constructor.
   - Test: assert `IOutboxWriter.EnqueueAsync` is called once, with `type ==
     OutboxMessageTypes.WorkNotification` and a payload whose `RecipientUserId == assignee.UserId` and
     `TemplateCode == "work_project_member_invited"`, on the happy-path invite. Assert it is **not**
     called on any of the short-circuit branches (already-member, conflict, forbidden, not-found).

6. **Wire `AcceptObjectiveInvitationCommandHandler`**: after the membership upsert succeeds, add
   `if (objective.IsDefault) { ... enqueue ... }`. Recipient is the inviter
   (`invitation.InvitedById`, an EmployeeId) — resolve their `Employee` (and thus `UserId`) via
   `IMilestoneMembershipCoordinator.GetActiveAssigneeAsync(tenantId, invitation.InvitedById, ct)`
   (same method Part 2 already uses for the same purpose); if somehow null (inviter deactivated since
   inviting), skip the notification silently rather than failing the whole accept — the invitee accepting
   should never fail because of the inviter's own account state. Resolve the accepter's display name via
   `ResolveDisplayNamesByEmployeeIdAsync(tenantId, [invitation.InvitedEmployeeId], ct)`. Payload template
   code `"work_project_member_accepted"`, placeholders `["accepterName"]`/`["projectName"]`
   (`project.Name` — load the Project if the handler doesn't already have it in scope; check first).
   - Tests: accepting a Default-Objective invitation → `EnqueueAsync` called once with the correct
     recipient/template. Accepting a **non-default** Objective invitation → `EnqueueAsync` **not**
     called (this is the critical regression-guard test — it's the one thing most likely to be gotten
     wrong by copy-pasting without the `IsDefault` gate). Inviter no longer an active employee → accept
     still succeeds, no notification sent, no exception.

7. **Postman docs**: no new endpoint was added in this part (notifications are a side effect of Part 2's
   endpoint and the existing accept endpoint), so no new `docs/postman-request/` file — but if Part 1/2's
   docs don't already mention "also sends an in-app notification", add a one-line note to
   `Add Project Member.md`'s description section.

## Data flow

Invite: `AddProjectMemberCommandHandler` creates the `ProjectMemberInvitation` row AND an `OutboxMessage`
row in the same `SaveChangesAsync` call → both commit together or neither does → `OutboxProcessor`
(background, up to ~10s later) picks up the pending message → `WorkNotificationOutboxHandler` decrypts
the payload → calls `INotificationDispatcher.SendTemplatedAsync` → a `Notification` row is created for
the invitee, visible next time they poll/load notifications.

Accept: `AcceptObjectiveInvitationCommandHandler`'s existing transaction (membership upsert + invitation
status update) gains one more write — the `OutboxMessage` row — conditional on `objective.IsDefault`,
still inside the same `ExecuteInTransactionAsync`/`SaveChangesAsync` as everything else in that handler.

## Security

No new attack surface: the Outbox payload carries only `RecipientUserId`/template code/placeholders that
are already derived from tenant-scoped, permission-checked data (the caller already passed the
project-owner gate in Part 2, and `AcceptObjectiveInvitationCommandHandler` already checks the invitee is
the one accepting). `WorkNotificationOutboxHandler` itself does not re-check permissions — by the time a
message reaches it, the originating command already enforced them; this matches how every other
`IOutboxMessageHandler` in the codebase works (they trust the producer, not the consumer, to gate access).

## Definition of done

- All 7 tasks committed individually.
- `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagement` green, including every
  new test above — the `IsDefault`-gate regression test in Task 6 is the one to double-check most
  carefully.
- Full solution `dotnet build` compiles clean.
- Whole `2026-08-20-work-management-project-page-redesign/` folder (all 3 parts) moved from
  `plans/next/` to `plans/finished/<completion-date>/`, `plans/SUMMARY.md` / `plans/next/SUMMARY.md` /
  `plans/finished/SUMMARY.md` updated, and this spec moved `specs/next/` → `specs/finished/<date>/` —
  only after the frontend plan (written separately, later) also ships, per this repo's existing
  finished/next convention. If backend ships well before frontend, it's fine to leave this in `next/`
  status `backend-done, frontend-pending` (add that note to `plans/next/SUMMARY.md`) rather than moving
  it prematurely.
