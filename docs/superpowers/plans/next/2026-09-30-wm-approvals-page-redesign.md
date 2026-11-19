# WM Approvals Page Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline, no subagents) to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the sectioned Approvals page with a single filterable list of every request the caller **sent or received** in the project (pending first). Selecting a row opens an **explanation card** that shows:
- the full detail (field-by-field "current → requested", invitation details, history);
- the approver's ability to **edit the values and then approve**;
- a **comment thread** (comment / reply / edit own) for requester ↔ approver negotiation.

**Architecture:** The backend gets:
- one **feed** endpoint (engine requests + module invitations, caller-scoped);
- one **detail** endpoint (field diff built server-side);
- a small **comments** feature (`wm_approval_comments`).

It also stops overwriting the requested payload when an approver edits it (a new `applied_payload_json`). The frontend rewrites `WorkApprovalsStore` around the feed and builds these on the existing `app-task-filter-bar`:
- a table component;
- an explanation-card component;
- a comments component.

The old section components are deleted.

**Tech Stack:**
- Backend: .NET 8, MediatR, EF Core (PostgreSQL + RLS), xUnit.
- Frontend: Angular 18 standalone components with signals, `@ngrx/signals`, Jasmine/Karma.

**Spec:** this document. The **Design** section below is the spec, and it travels with the plan. The visual reference is `docs/superpowers/plans/next/assets/2026-09-30-approvals-redesign-reference.webp`; open it before Part C.

## Global Constraints

- **Project- and user-oriented (user rule, 2026-09-30).** Every project has its own Approvals page (`/work/:projectId/approvals`). The page shows only that project's requests and invitations, and only those the logged-in user sent or received (D1).
  - No cross-project rows.
  - No rows between two other people.
  - Every backend query filters by `projectId` and the caller's employee id. Every frontend load passes the route's project id.

- **Work Management only.**
  - Never edit CoreHr, Leave, TimeAttendance, People, Calendar, Auth features, or shared notification/outbox code.
  - The only shared touchpoint is `NotificationTemplateSeeder.cs`, where one WM template is added, and its seeder test count goes 40 → 41.
- **Repos and branches:**
  - Backend: `C:\Users\User\Desktop\build\HRMS-Backend-v1`.
  - Frontend: `C:\Users\User\Desktop\build\Hrms--Web-application---front-end---v1`.
  - Create a new branch `feature/wm-approvals-redesign` in both repos **from the current HEAD of `feature/wm-hierarchy-approval-notification-engine`**. This work depends on that unmerged engine.
- **Colours:** theme tokens only (`var(--color-*)`). Zero hard-coded hex colours in the new or changed components.
  - For status tints use `color-mix(in srgb, var(--color-success) 14%, transparent)` and the like. The theme has no `--color-success-subtle`.
  - `--color-accent` follows the user's chosen accent. Use it for primary buttons, the selected row, and links.
- **Icons:** inline SVG icons (stroke `currentColor`, `viewBox="0 0 24 24"`), the same way `task-filter-bar.component.ts` draws them. No icon font and no new icon library.
- **Filters:** reuse `app-task-filter-bar` (`src/app/modules/work/ui/task-filter-bar`). Do not write a new filter UI.
- **Reject reason is optional.** Clicking Reject opens a reminder dialog asking for a reason, and the user may reject without one. The backend already allows an empty comment.
- **3-dot quick actions never include Reject.**
- **TDD:** failing test → implement → green → commit.
- **Commit messages** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Git limits:**
  - Do NOT push, open PRs, or run `dotnet ef database update`. Generating the migration is fine.
  - Never stash, reset, or discard user work.
  - Never junction `node_modules` into a temporary worktree.
- **Gates:**
  - **Backend:** `dotnet build src/ONEVO.Api -c Release`, `dotnet test tests/ONEVO.Tests.Unit -c Release`, `dotnet test tests/ONEVO.Tests.Architecture -c Release`.
  - **Frontend:** `npx ng build` and `npx ng test --watch=false --browsers=ChromeHeadless`.
  - The frontend suite is flaky under load (flip-animate, milestone-tree-tab, and the old work-approvals specs). If a failure passes in isolation, record it; don't "fix" it.
- **Useless-test rule:** delete a test only when it tests deleted code, duplicates another test, or asserts nothing behavioural. List every deleted spec or test file in its commit message.

---

## Design (the spec)

### What the user asked for (verbatim intent, translated)

1. **One list.** It holds every request: not-yet-decided ones **on top**, decided ones below.
2. **Filters** use the existing filter component, with every suitable field.
3. **Explanation card** opens when a row is selected. It shows:
   - what the thing looked like before, and what was edited;
   - the full invitation detail when the row is an invitation.
4. **The approver can edit the requested values in the card** and apply them. Example: an employee requests a task edit; the parent edits the values directly in the card and approves.
5. **Sent and received both live here.** The page shows the requests the user sent, with their status. After approval or rejection they stay visible, so this is also the history.
6. **Comments on every request, in the explanation card.** Create, reply, and edit (only your own comment) let the requester and approver negotiate.
7. **3-dot quick actions** on each row exclude Reject.
8. **Reject reason is no longer mandatory.** Rejecting prompts: "add the reason as a comment?" The user can decline and still reject.
9. Match the reference image, with theme colours and icons everywhere.

### Decisions taken (change them here before executing if wrong)

- **D1. Scope of "sent / received".**
  - **Sent:** every engine request the caller made in this project, plus module invitations the caller sent.
  - **Received:** every engine request the caller can decide now (`WorkApprovalDecisionRules.CanDecide`), was the resolved approver of (`ApproverEmployeeId`), or decided (`DecidedByEmployeeId`). Received also includes module invitations sent **to** the caller.
  - Decided rows stay visible. This is the history.
- **D2. Invitations reuse their own table.** They are not moved into `wm_approval_requests`. In the feed they appear with `source = "invitation"` and `actionType = "module.invitation"`, and their statuses are mapped:
  - accepted → `approved`;
  - declined → `rejected`;
  - expired → `stale`;
  - pending and cancelled stay as they are.

  The UI labels invitation outcomes "Accepted", "Declined" and "Expired".
- **D3. The field diff is built on the backend.** Payloads are inconsistent: task payloads are PascalCase, module and sprint payloads are camelCase. The backend therefore returns `fields[]`, where `key` is the property name **exactly as stored** in the payload. The frontend overwrites those keys to build `editedPayloadJson`.
- **D4. Approver edits are kept, not lost.** `DecideWorkApprovalRequestCommandHandler` currently overwrites `PayloadJson` with the edited payload. Instead:
  - `PayloadJson` stays = requested;
  - the new nullable `AppliedPayloadJson` = what was actually applied (set only when it differs from the requested payload).

  The card shows **Current / Requested / Approved**.
- **D5. Editable actions:**
  - `task.create`, `task.edit`, `module.edit`, `module.allocation_extend`, `sprint.create`, `sprint.edit`;
  - only while pending, and only for a caller who can decide.
  - Everything else (delete, transfer, achieve, unachieve, start, complete, status template) is approve/reject only.
- **D6. Comments.** The new table `wm_approval_comments` stores:
  - `subject_type` (`approval` | `invitation`) + `subject_id`;
  - one reply level (a reply to a reply attaches to the top-level parent — same rule as task comments);
  - author-only edit with an `is_edited` flag.

  There is no delete and there are no reactions (not asked for).
  - **Who may read or write:** the requester and every "received" party from D1. For an invitation, the inviter and the invitee.
  - Comments stay allowed after the decision; the thread is part of the history.
  - Each comment notifies the other participants: requester, `ApproverEmployeeId`, and the decider if any. Invitations notify inviter and invitee. It uses the new kind `commented` and the template `work_approval_commented`.
- **D7. The reject reason** is still saved as `DecisionComment`, so the history keeps it. It is **not** duplicated into the thread.
- **D8. The status-template panel** (`task-status-change-requests-panel`) is removed from the page.
  - `project.status_template_change` rows become ordinary feed rows (type "Status template"). They are approved or rejected through the generic engine endpoints; the applier already exists.
  - The card shows the change summary line: "2 added, 1 edited".
  - The panel component is deleted only if nothing else imports it. At plan time it has no other consumer.
- **D9. Paging is client-side.** 10 rows per page by default; 10, 20 or 50 selectable. Project request volumes are small, and the feed returns everything for the caller in one call.
- **D10. Deep link:** `?request=<id>` opens the card for that row on load. The old `?tab=history` is ignored harmlessly (it has no tabs now).
- **D11. Quick actions (3-dot)**, shown when they apply:
  - `View details`;
  - `Approve` (as requested, no edits), or `Accept` for an invitation;
  - `Open task` / `Open module`;
  - `Cancel request` (the caller's own pending engine request);
  - `Copy link`.

  Never Reject, and never Decline an invitation.

### Page layout (from the reference image)

- **Header:** "Approvals" h1 + "Review and take action on all requests." The project toolbar (right side, via `WorkPageToolbarStore`) holds `Filter` (app-task-filter-bar) + `Create Approval` (kept, same modal as today).
- **Table columns:**
  - Title: type icon tile + title + one-line subtitle;
  - Type: coloured pill;
  - Module / Project: module title + project name;
  - Requested by: avatar + name;
  - Requested on: date + time;
  - Status: pill;
  - Actions: 3-dot.
- **Row behaviour:** the selected row gets an accent outline and a subtle accent background. Pending rows come first, each group newest first.
- **Footer:** "Showing a–b of N requests", page buttons, and a "10 / page" select.
- **Explanation card** (right, ~480px wide, sticky, close ×):
  - type label + status pill;
  - title + subtitle;
  - **Details** box: Module, Project, Requested by (avatar, name), Requested on, Approver, and when decided: Decided by + on + reason;
  - **Invitation** box, for invitations: invitee, invited as (member or leader), module, invited by, expires;
  - **Changes** table: Field / Current / Requested / Approve value (editable inputs when D5 applies). After the decision the last column becomes "Approved". Highlight the requested cells that differ from current;
  - **Reason / note** from the payload when present;
  - **Activity:**
    - timeline: Requested → (Commented…) → Approved or Rejected or Cancelled or Stale, with who and when, plus "Approved with changes" when `appliedPayloadJson` ≠ null;
    - comment thread + composer.
  - **Footer (sticky):**
    - approver + pending: `Reject` (danger outline) + `Approve` (primary);
    - invitee + pending: `Decline` + `Accept`;
    - requester + pending: `Cancel request`;
    - otherwise no buttons.
- **Below 1100px** the card becomes a full-height overlay drawer over the table.

---

## File map

### Backend (`HRMS-Backend-v1`)

| Action | Path |
|---|---|
| Modify | `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs` (+`AppliedPayloadJson`) |
| Create | `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalComment.cs` |
| Modify | `src/ONEVO.Domain/Features/WorkManagement/Notifications/Entities/WorkNotificationLog.cs` (+`Commented` kind) |
| Modify | `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalRequestConfiguration.cs` |
| Create | `src/ONEVO.Infrastructure/Persistence/Configurations/WorkManagement/WorkApprovalCommentConfiguration.cs` |
| Modify | `src/ONEVO.Infrastructure/Persistence/ApplicationDbContext.cs` (+DbSet) |
| Create | migration `AddApprovalCommentsAndAppliedPayload` (+ RLS block) |
| Modify | `src/ONEVO.Application/Features/WorkManagement/Approvals/Commands/DecideWorkApprovalRequest/DecideWorkApprovalRequestCommandHandler.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalFeedParticipants.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalChangeSetBuilder.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/ApprovalFeedItemResponse.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/ApprovalDetailResponse.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/DTOs/ApprovalCommentResponse.cs` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/GetProjectApprovalFeed/*` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/Queries/GetApprovalDetail/*` |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/Comments/*` (Create, Reply, Edit, List + access service) |
| Create | `src/ONEVO.Application/Features/WorkManagement/Approvals/RepositoryInterfaces/IWorkApprovalCommentRepository.cs` |
| Create | `src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfWorkApprovalCommentRepository.cs` |
| Modify | `IProjectMemberInvitationRepository` + Ef impl (+`ListForProjectAndEmployeeAsync`) |
| Modify | `src/ONEVO.Application/Features/WorkManagement/Notifications/Services/WorkNotificationEngine.cs` (+commented template) |
| Modify | `src/ONEVO.Infrastructure/Persistence/Seeders/NotificationTemplateSeeder.cs` (+1 template) |
| Modify | `src/ONEVO.Infrastructure/DependencyInjection.cs` (+comment repo) |
| Modify | `src/ONEVO.Api/Controllers/Tenant/WorkManagement/WorkApprovalsController.cs` (+feed, detail, comment routes) |
| Tests | `tests/ONEVO.Tests.Unit/Features/WorkManagement/Approvals/...` (mirror existing folder of `DecideWorkApprovalRequestCommandHandlerTests`) |

### Frontend (`Hrms--Web-application---front-end---v1/src/app/modules/work`)

| Action | Path |
|---|---|
| Create | `models/dto/approval-feed.dto.ts` |
| Modify | `data-access/work-approvals-api.service.ts` (+feed, detail, comments) |
| Create | `utils/approval-feed.util.ts` (+spec): type meta, sort, filter predicate, filter field config |
| Rewrite | `state/work-approvals.store.ts` (+spec) |
| Modify | `feature/project-detail/project-detail.component.ts` (badge count from new store) |
| Create | `ui/approval-type-icon/approval-type-icon.component.ts` |
| Create | `ui/approval-table/approval-table.component.ts` (+spec) |
| Create | `ui/approval-explanation-card/approval-explanation-card.component.ts` (+spec) |
| Create | `ui/approval-comments/approval-comments.component.ts` (+spec) |
| Create | `ui/approval-reject-dialog/approval-reject-dialog.component.ts` (+spec) |
| Rewrite | `feature/work-approvals/work-approvals.component.ts` (+spec) |
| Delete | `ui/approval-request-row/*`, `ui/approval-history-list/*`, `ui/task-status-change-requests-panel/*` (only if no other importer), `utils/approval.mapper.ts` + `models/approval.model.ts` (only if no other importer) |

---

## Part A — Backend data + decision

### Task 1: Schema — `AppliedPayloadJson` + `WorkApprovalComment`

**Files:**
- Modify: `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalRequest.cs`
- Create: `src/ONEVO.Domain/Features/WorkManagement/Approvals/Entities/WorkApprovalComment.cs`
- Modify: `WorkApprovalRequestConfiguration.cs`, `ApplicationDbContext.cs`
- Create: `WorkApprovalCommentConfiguration.cs`, migration

**Interfaces:**
- Produces:
  - `WorkApprovalRequest.AppliedPayloadJson : string?`
  - `WorkApprovalComment { SubjectType, SubjectId, ProjectId, EmployeeId, ParentCommentId?, Content, IsEdited }`
  - `WorkApprovalCommentSubjects.Approval = "approval"`, `WorkApprovalCommentSubjects.Invitation = "invitation"`

- [ ] **Step 1: Add the property to `WorkApprovalRequest`** (below `PayloadJson`):

```csharp
    /// <summary>The payload actually applied, set only when the approver changed the requested values
    /// before approving. PayloadJson always keeps what was requested.</summary>
    public string? AppliedPayloadJson { get; set; }
```

- [ ] **Step 2: Create the comment entity**

```csharp
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

public static class WorkApprovalCommentSubjects
{
    public const string Approval = "approval";
    public const string Invitation = "invitation";
}

/// <summary>A comment (or, when ParentCommentId is set, a reply) on an approval request or a module
/// invitation, so the requester and the approver can negotiate. Replies always target a top-level
/// comment - enforced by the command handler, like TaskComment.</summary>
public class WorkApprovalComment : BaseEntity
{
    public string SubjectType { get; set; } = WorkApprovalCommentSubjects.Approval;
    public Guid SubjectId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsEdited { get; set; }
}
```

- [ ] **Step 3: Configuration**
  - Copy the style of `WorkApprovalRequestConfiguration.cs`: snake_case, table `wm_approval_comments`, `content` max 4000, `subject_type` max 20.
  - Index `(tenant_id, subject_type, subject_id, created_at)`.
  - Add the soft-delete query filter the same way the other WM configs do.
  - In `WorkApprovalRequestConfiguration` map `AppliedPayloadJson` → `applied_payload_json`, using the same column type as `payload_json` (check whether it's `jsonb` or `text` and copy it).
  - Add `public DbSet<WorkApprovalComment> WorkApprovalComments => Set<WorkApprovalComment>();` in `ApplicationDbContext` next to the `WorkApprovalRequests` set, in the same style.

- [ ] **Step 4: Generate the migration**

```powershell
$env:ConnectionStrings__MigrationConnection = "Host=localhost;Database=dummy;Username=x;Password=x"
dotnet ef migrations add AddApprovalCommentsAndAppliedPayload --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release
```

Then **add the RLS block** to the migration: copy the `TenantTables` array and both `foreach` loops (Up and Down) verbatim from `20260922071349_AddTaskComments.cs`, with `TenantTables = ["wm_approval_comments"]`. Without it the architecture RLS-coverage test fails.

- [ ] **Step 5: Verify**

Run:
- `dotnet ef migrations has-pending-model-changes --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api --configuration Release` → expect no pending changes.
- `dotnet test tests/ONEVO.Tests.Architecture -c Release` → expect all green.

Check the generated migration touches **only** these two tables. If it touches unrelated tables, the snapshot is corrupted (see memory `feedback_hrms_migration_snapshot_corruption`): stop and diagnose.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain src/ONEVO.Infrastructure
git commit -m "feat(work): approval comments table and applied payload column

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

### Task 2: Decision keeps the requested payload

**Files:**
- Modify: `DecideWorkApprovalRequestCommandHandler.cs`
- Test: the existing `DecideWorkApprovalRequestCommandHandlerTests` (find it with `grep -rl DecideWorkApprovalRequestCommandHandler tests`)

**Interfaces:** Produces: after an approve with an edited payload, `PayloadJson` = original and `AppliedPayloadJson` = edited; after an approve without edits, `AppliedPayloadJson` = null.

- [ ] **Step 1: Write failing tests** in the existing test class. Follow its arrange helpers and fake applier.

```csharp
[Fact]
public async Task Approve_with_edited_payload_keeps_requested_payload_and_stores_applied()
{
    // arrange a pending task.edit request with PayloadJson = """{"Title":"A"}""" and an applier returning Applied
    // act: Approve with EditedPayloadJson = """{"Title":"B"}"""
    // assert
    Assert.Equal("""{"Title":"A"}""", request.PayloadJson);
    Assert.Equal("""{"Title":"B"}""", request.AppliedPayloadJson);
    Assert.Equal(WorkApprovalRequestStatuses.Approved, request.Status);
}

[Fact]
public async Task Approve_without_edits_leaves_applied_payload_null()
{
    // same arrange, Approve with EditedPayloadJson = null
    Assert.Null(request.AppliedPayloadJson);
}
```

Fill in the arrange and act lines using the class's existing helpers. Don't invent new fakes.

- [ ] **Step 2:** Run `dotnet test tests/ONEVO.Tests.Unit -c Release --filter DecideWorkApprovalRequestCommandHandler`. Expect: the first test FAILS (PayloadJson was overwritten).

- [ ] **Step 3: Implement.** In the handler replace

```csharp
                    if (outcome.Kind == ApplyOutcomeKind.Applied)
                        request.PayloadJson = payload;
```

with

```csharp
                    if (outcome.Kind == ApplyOutcomeKind.Applied && payload != request.PayloadJson)
                        request.AppliedPayloadJson = payload;
```

Then grep for any reader that relied on the overwrite: `grep -rn "PayloadJson" src/ONEVO.Application/Features/WorkManagement`.
- Readers of **decided** rows that need the applied values (e.g. history detail builders) should read `AppliedPayloadJson ?? PayloadJson`.
- If there are none, change nothing else.

- [ ] **Step 4:** Run the same filter → PASS. Then run the full unit suite → green.

- [ ] **Step 5: Commit**: `fix(work): keep the requested payload when the approver edits before approving`

---

## Part B — Backend read API + comments

### Task 3: Participants rule + invitation lookup

**Files:**
- Create: `src/ONEVO.Application/Features/WorkManagement/Approvals/Services/ApprovalFeedParticipants.cs`
- Modify: `IProjectMemberInvitationRepository.cs` + its Ef implementation (find it with `grep -rl "class EfProjectMemberInvitationRepository" src`)
- Test: `tests/ONEVO.Tests.Unit/.../Approvals/ApprovalFeedParticipantsTests.cs`

**Interfaces:**
- Produces:
  - `static bool ApprovalFeedParticipants.IsSender(WorkApprovalRequest r, Guid caller)`
  - `static bool ApprovalFeedParticipants.IsReceiver(ProjectModuleTree tree, WorkApprovalRequest r, Guid caller)`
  - `static IReadOnlyCollection<Guid> ApprovalFeedParticipants.NotifyTargets(WorkApprovalRequest r)` → requester, approver, decider (distinct, non-null)
  - `Task<IReadOnlyList<ProjectMemberInvitation>> ListForProjectAndEmployeeAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct)`: invitations in the project where the employee is the invitee OR the inviter, newest first.

- [ ] **Step 1: Failing tests**

```csharp
public class ApprovalFeedParticipantsTests
{
    private static readonly Guid Requester = Guid.NewGuid(), Approver = Guid.NewGuid(), Stranger = Guid.NewGuid();

    private static WorkApprovalRequest Req(string status = WorkApprovalRequestStatuses.Pending, Guid? decidedBy = null) => new()
    {
        Id = Guid.NewGuid(), RequestedByEmployeeId = Requester, ApproverEmployeeId = Approver,
        ApproverSource = WorkApprovalSources.Hr, Status = status, DecidedByEmployeeId = decidedBy
    };

    [Fact] public void Requester_is_sender_not_receiver()
    {
        var r = Req();
        Assert.True(ApprovalFeedParticipants.IsSender(r, Requester));
        Assert.False(ApprovalFeedParticipants.IsReceiver(new ProjectModuleTree([]), r, Requester));
    }

    [Fact] public void Resolved_approver_is_receiver_even_after_decision()
        => Assert.True(ApprovalFeedParticipants.IsReceiver(new ProjectModuleTree([]), Req(WorkApprovalRequestStatuses.Approved, Approver), Approver));

    [Fact] public void Decider_is_receiver()
    {
        var decider = Guid.NewGuid();
        Assert.True(ApprovalFeedParticipants.IsReceiver(new ProjectModuleTree([]), Req(WorkApprovalRequestStatuses.Rejected, decider), decider));
    }

    [Fact] public void Stranger_is_neither()
    {
        var r = Req();
        Assert.False(ApprovalFeedParticipants.IsSender(r, Stranger));
        Assert.False(ApprovalFeedParticipants.IsReceiver(new ProjectModuleTree([]), r, Stranger));
    }

    [Fact] public void Notify_targets_are_distinct_participants()
        => Assert.Equal(new[] { Requester, Approver }, ApprovalFeedParticipants.NotifyTargets(Req()).OrderBy(g => g == Approver));
}
```

Also add one test for a **hierarchy** request where the caller owns an ancestor module. Build the `ProjectModuleTree` the way `WorkApprovalDecisionRulesTests` does (copy its builder) and assert `IsReceiver` = true.

- [ ] **Step 2:** Run → FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

/// <summary>Who sees an approval request on the Approvals page and in its comment thread.
/// Sender = the requester. Receiver = anyone who can decide it now, its resolved approver, or whoever decided it.</summary>
public static class ApprovalFeedParticipants
{
    public static bool IsSender(WorkApprovalRequest r, Guid caller) => r.RequestedByEmployeeId == caller;

    public static bool IsReceiver(ProjectModuleTree tree, WorkApprovalRequest r, Guid caller)
        => caller != r.RequestedByEmployeeId
           && (r.ApproverEmployeeId == caller
               || r.DecidedByEmployeeId == caller
               || WorkApprovalDecisionRules.CanDecide(tree, r, caller));

    public static bool CanSee(ProjectModuleTree tree, WorkApprovalRequest r, Guid caller)
        => IsSender(r, caller) || IsReceiver(tree, r, caller);

    public static IReadOnlyCollection<Guid> NotifyTargets(WorkApprovalRequest r)
        => new[] { r.RequestedByEmployeeId, r.ApproverEmployeeId, r.DecidedByEmployeeId ?? Guid.Empty }
            .Where(id => id != Guid.Empty).Distinct().ToList();
}
```

Add the repository method. The EF body uses the same `AsNoTracking` and tenant filter style as `ListPendingForEmployeeAsync`:

```csharp
    public async Task<IReadOnlyList<ProjectMemberInvitation>> ListForProjectAndEmployeeAsync(
        Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
        => await _db.ProjectMemberInvitations.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.ProjectId == projectId
                        && (i.InvitedEmployeeId == employeeId || i.InvitedById == employeeId))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
```

(Use the DbSet and field names the class actually has.)

- [ ] **Step 4:** Run tests → PASS.
- [ ] **Step 5: Commit**: `feat(work): approval feed participant rules and invitation lookup`

### Task 4: Feed query `GET /work/projects/{projectId}/approval-feed`

**Files:**
- Create: `Approvals/DTOs/ApprovalFeedItemResponse.cs`, `Approvals/Queries/GetProjectApprovalFeed/GetProjectApprovalFeedQuery.cs` + handler
- Modify: `WorkApprovalsController.cs`
- Test: `GetProjectApprovalFeedQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `ApprovalFeedParticipants`, `ListForProjectAndEmployeeAsync`, `IWorkApprovalRequestRepository.ListByProjectAsync(tenant, project, null, null)`, `IWorkTaskRepository.GetObjectiveIdsByTaskIdsAsync`, `IWorkHierarchyService.LoadTreeAsync`, `ICallerIdentityResolver.ResolveDisplayNamesByEmployeeIdAsync`.
- Produces:

```csharp
namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public static class ApprovalFeedSources
{
    public const string Engine = "engine";
    public const string Invitation = "invitation";
}

/// <summary>One row of the Approvals page. Direction is from the caller's side. Status uses the engine
/// vocabulary (pending/approved/rejected/cancelled/stale) - invitations are mapped onto it.</summary>
public sealed record ApprovalFeedItemResponse(
    Guid Id,
    string Source,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid? ModuleId,
    string? ModuleTitle,
    string ProjectName,
    string Status,
    string Direction,
    Guid RequestedById,
    string RequestedByName,
    Guid ApproverId,
    string ApproverName,
    Guid? DecidedById,
    string? DecidedByName,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    bool CanDecide,
    bool CanCancel,
    int CommentCount,
    string? Summary);
```

Rules in the handler:
- **Engine rows:**
  - Load all project requests, then keep rows where `IsSender || IsReceiver`.
  - `Direction` = sender ? `"sent"` : `"received"`.
  - `CanDecide` = pending && `WorkApprovalDecisionRules.CanDecide`.
  - `CanCancel` = pending && sender.
- **ModuleId:**
  - module target → `TargetId`;
  - `task.create` → `objectiveId`/`ObjectiveId` from the payload (case-insensitive `JsonDocument` read);
  - other task targets → `GetObjectiveIdsByTaskIdsAsync` (batch; missing = deleted task → fall back to `PositionObjectiveId`);
  - sprint/project → `PositionObjectiveId`.
- **ModuleTitle** = `tree.Get(moduleId)?.Title`. For the root module use the project name.
- **ProjectName:** load the project once (the WM project repository's `GetByIdForTenantAsync`; find it with `grep -rn "interface IProjectRepository" src`). If the project is missing, return `NotFound`.
- **Summary:** one short line per action:
  - allocation → `"+{hours}h - {reason}"`;
  - status template → reuse the adds/updates/deletes counting in `GetWorkApprovalHistoryQueryHandler.DescribeStatusChanges`. Move that helper into a small `internal static class ApprovalSummaries` in `Approvals/Services` and call it from both places;
  - task/module edit → `"Proposed title: X"` when the title changed;
  - otherwise null.
- **Invitation rows:** `Source = invitation`, `ActionType = "module.invitation"`, `TargetType = "module"`, `TargetId = ObjectiveId`, `TargetTitle = tree.Get(ObjectiveId)?.Title ?? "Module"`, `RequestedById = InvitedById`, `ApproverId = InvitedEmployeeId`, `DecidedById` = invitee when decided, `Summary = "Invited as {InviteType}"`, `CanDecide` = pending && invitee == caller, `CanCancel = false`. Status map as in D2.
- **CommentCount:** `IWorkApprovalCommentRepository.CountBySubjectsAsync` (created in Task 6).
  - To keep this task self-contained, add the interface method now (see Task 6's interface) with the EF implementation.
  - Or temporarily return 0 and wire it in Task 6. **Pick the second option** and add a failing test in Task 6 that asserts the count.
- **Sort:** pending first, then `CreatedAt` desc.

- [ ] **Step 1: Failing tests.** Use the fakes/builders already used by `ListProjectWorkApprovalsQueryHandlerTests` (find it with `grep -rl ListProjectWorkApprovalsQueryHandler tests`). Cases:
  1. Returns the caller's sent request with `Direction = "sent"`, `CanCancel = true`, `CanDecide = false`.
  2. Returns a request the caller can decide with `Direction = "received"`, `CanDecide = true`.
  3. Hides a request between two other people.
  4. Keeps a decided request the caller decided (`received`, `CanDecide = false`).
  5. Maps an accepted invitation to `approved` and `ActionType = "module.invitation"`; the invitee sees it as `received`.
  6. Orders pending before decided.
  7. `task.edit` row gets `ModuleId` from `GetObjectiveIdsByTaskIdsAsync`.

- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement** the query, handler, and DTO as specified above. Query record:

```csharp
public sealed record GetProjectApprovalFeedQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<ApprovalFeedItemResponse>>>;
```

Controller action (in `WorkApprovalsController`, next to `List`):

```csharp
    /// <summary>Every request the caller sent or received in this project (engine requests + module invitations), pending first.</summary>
    [HttpGet("projects/{projectId:guid}/approval-feed")]
    public async Task<IActionResult> Feed(Guid projectId, CancellationToken ct)
        => ToResult(await _mediator.Send(new GetProjectApprovalFeedQuery(projectId), ct));
```

- [ ] **Step 4:** Tests → PASS, then build the API in Release.
- [ ] **Step 5: Commit**: `feat(work): approvals feed endpoint (sent + received, incl. invitations)`

### Task 5: Detail query `GET /work/approvals/{id}?source=engine|invitation`

**Files:**
- Create: `Approvals/Services/ApprovalChangeSetBuilder.cs`, `Approvals/DTOs/ApprovalDetailResponse.cs`, `Approvals/Queries/GetApprovalDetail/*`
- Test: `ApprovalChangeSetBuilderTests.cs`, `GetApprovalDetailQueryHandlerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record ApprovalFieldResponse(
    string Key,        // property name exactly as stored in PayloadJson (e.g. "Title" or "title") - the frontend overwrites this key
    string Label,
    string Kind,       // "text" | "longtext" | "number" | "hours" | "date" | "priority" | "employee" | "sprint"
    string? Current,   // display-ready string, null = none
    string? Requested,
    string? Applied,   // from AppliedPayloadJson; null when not decided or not edited
    bool Changed,      // Requested != Current
    bool Editable);

public sealed record ApprovalInvitationResponse(
    Guid InviteeId, string InviteeName, string InviteType, Guid InvitedById, string InvitedByName,
    Guid ModuleId, string ModuleTitle, DateTimeOffset? ExpiresAt);

public sealed record ApprovalDetailResponse(
    ApprovalFeedItemResponse Item,
    string? RequestedPayloadJson,
    string? AppliedPayloadJson,
    IReadOnlyList<ApprovalFieldResponse> Fields,
    bool CanEditPayload,
    string? Note,                       // the payload's reason/note, if any
    decimal? CurrentAllocatedHours,     // allocation rows only
    ApprovalInvitationResponse? Invitation);
```

**ApprovalChangeSetBuilder rules.** It is pure: input = actionType, requested JSON, applied JSON, a `IReadOnlyDictionary<string, string?> current` (keys camelCase), and `editable`. The field specs per action are below: *(camelName, Label, Kind)*.
- `task.create` / `task.edit`:
  - (title, Title, text), (description, Description, longtext), (priority, Priority, priority), (dueDate, Due date, date), (estimatedHours, Estimated hours, hours), (storyPoints, Story points, number);
  - task.edit also has (progressPercent, Progress %, number).
  - `task.create` current values are all null.
- `module.edit`: (title, Title, text), (description, Description, longtext), (startDate, Start date, date), (endDate, End date, date), (allocatedHours, Allocated hours, hours).
- `module.allocation_extend`: (requestedAdditionalHours, Additional hours, hours). Current is null; the detail response carries `CurrentAllocatedHours` separately.
- `sprint.create`: (name, Name, text), (goal, Goal, longtext).
- `sprint.edit`: (name, Name, text), (goal, Goal, longtext), (startDate, Start date, date), (endDate, End date, date).
- `sprint.start`: (startDate, Start date, date), (endDate, End date, date), (goal, Goal, longtext). Never editable.
- `module.transfer`: read the transfer input record in `ModuleActionPayloads.cs`. Emit a single (newHeadEmployeeId or its actual name, New owner, employee) field. The handler resolves the employee id to a display name before calling the builder: pass the requested/current display names in through `current` and a `requestedDisplay` override map. Keep it simple — an optional `IReadOnlyDictionary<string,string?>? requestedOverrides` parameter.
- Everything else: no fields.
- **Key resolution:** find the property in the requested JSON case-insensitively. `Key` = the name as found. When absent, `Key` = the camelName and `Requested` = null.
- **Formatting:** dates as `yyyy-MM-dd`; numbers invariant culture with `0.##`; strings trimmed; JSON null → null.
- `Changed` = `!string.Equals(Current, Requested)`. For `task.create`, `sprint.create`, and allocation, `Changed` = `Requested != null`.
- `Editable` = the `editable` argument for every field of an editable action (D5), except `sprint.start`, `module.transfer`, and `longtext` fields (still shown, not editable in v1 — rich text editing is out of scope).

- [ ] **Step 1: Failing builder tests:**
  - `task.edit` with PascalCase payload `{"Title":"New","Priority":"High","DueDate":"2026-10-05"}` and current `{title:"Old", priority:"High", dueDate:"2026-10-01"}`:
    - Title field `Key == "Title"`, `Changed == true`;
    - Priority `Changed == false`;
    - DueDate requested `"2026-10-05"`.
  - `module.edit` camelCase with an applied payload → `Applied` set only on fields present in the applied JSON.
  - Allocation → one field, `Key == "requestedAdditionalHours"`, `Requested == "35"`.
  - Unknown action → empty list.
  - `editable: false` → all `Editable == false`.

- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement the builder. **Step 4:** PASS.

- [ ] **Step 5: Detail handler** (`GetApprovalDetailQuery(Guid Id, string Source)`).

  **Engine source:**
  - Load the request (add a non-tracked `GetByIdForTenantAsync` to `IWorkApprovalRequestRepository` if only the tracked one exists) and load the tree.
  - If `!ApprovalFeedParticipants.CanSee` → **NotFound** (don't leak existence).
  - **Current values:**
    - task target: load the task (`IWorkTaskRepository.GetByIdForTenantAsync`) → title, description, priority, dueDate, estimatedHours, storyPoints, progressPercent;
    - module target: `tree.Get(id)` → title, description, startDate, endDate, allocatedHours;
    - sprint target: `ISprintRepository.GetByIdForTenantAsync` → name, goal, startDate, endDate. Use the real entity property names; check the entities.
  - **Feed item:** build it with the same code as Task 4. Extract a private static `ApprovalFeedItemFactory` in `Approvals/Services`, used by both handlers. That refactor is part of this task; Task 4's tests must stay green.
  - `CanEditPayload` = item.CanDecide && action ∈ D5 set.
  - `Note` = payload `reason`/`Reason`/`note`/`Note`.
  - `CurrentAllocatedHours` = tree module allocated hours for allocation rows.

  **Invitation source:**
  - Load the invitation; the caller must be the invitee or the inviter, else NotFound.
  - `Fields` = empty; `Invitation` filled with names.
  - Controller:

```csharp
    [HttpGet("approvals/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, [FromQuery] string source = "engine", CancellationToken ct = default)
        => ToResult(await _mediator.Send(new GetApprovalDetailQuery(id, source), ct));
```

  Handler tests: a stranger gets NotFound; the approver sees `CanEditPayload` true for a pending task.edit; the requester sees `CanEditPayload` false; an invitation returns an `Invitation` block and no fields.

- [ ] **Step 6:** Full unit suite green → **Commit**: `feat(work): approval detail endpoint with server-side field diff`

### Task 6: Approval comments (list / create / reply / edit) + notification

**Files:**
- Create: `Approvals/RepositoryInterfaces/IWorkApprovalCommentRepository.cs`, `Infrastructure/.../EfWorkApprovalCommentRepository.cs`, `Approvals/Comments/ApprovalCommentAccess.cs`, `Approvals/Comments/Commands/{CreateApprovalComment,ReplyApprovalComment,EditApprovalComment}/*`, `Approvals/Comments/Queries/ListApprovalComments/*`, `Approvals/DTOs/ApprovalCommentResponse.cs`
- Modify: `WorkNotificationLog.cs` (`Commented = "commented"`), `WorkNotificationEngine.cs`, `NotificationTemplateSeeder.cs`, `NotificationTemplateSeederTests.cs` (40 → 41 + code list), `DependencyInjection.cs`, `WorkApprovalsController.cs`, `GetProjectApprovalFeed` handler (comment counts)

**Interfaces:**

```csharp
public interface IWorkApprovalCommentRepository
{
    Task AddAsync(WorkApprovalComment comment, CancellationToken ct = default);
    Task<WorkApprovalComment?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<WorkApprovalComment>> ListBySubjectAsync(Guid tenantId, string subjectType, Guid subjectId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> CountBySubjectsAsync(Guid tenantId, string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default);
    void Update(WorkApprovalComment comment);
}

public sealed record ApprovalCommentResponse(
    Guid Id, Guid? ParentCommentId, Guid AuthorId, string AuthorName, string Content,
    bool IsEdited, bool CanEdit, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

/// <summary>Resolves the subject and says whether the caller may read/write its thread.</summary>
public interface IApprovalCommentAccess
{
    Task<ApprovalCommentSubject?> ResolveAsync(Guid tenantId, string subjectType, Guid subjectId, Guid caller, CancellationToken ct);
}

/// <summary>Participants = who gets notified of a new comment (caller excluded by the engine).</summary>
public sealed record ApprovalCommentSubject(
    string SubjectType, Guid SubjectId, Guid ProjectId, string ActionType, string TargetType,
    Guid? TargetId, string TargetTitle, Guid? ApprovalRequestId, IReadOnlyCollection<Guid> Participants);
```

**Access rules:**
- `approval` subject: load the request and the tree. Visible iff `ApprovalFeedParticipants.CanSee`. Participants = `NotifyTargets(r)`.
- `invitation` subject: the caller must be the invitee or the inviter. Participants = both. `ActionType = "module.invitation"`, `TargetType = "module"`, `ApprovalRequestId = null`.
- Not visible → the handler returns **NotFound**.

**Commands:**
- `CreateApprovalCommentCommand(string SubjectType, Guid SubjectId, string Content)`.
- `ReplyApprovalCommentCommand(Guid ParentCommentId, string Content)`: resolve the parent. If the parent itself has a `ParentCommentId`, attach to the grandparent (one level). Check access on the parent's subject.
- `EditApprovalCommentCommand(Guid CommentId, string Content)`: only the author (else Forbidden). Sets `IsEdited = true` and `UpdatedAt`.
- **Validation:** trimmed content 1–4000 chars, else `Failure("Comment cannot be empty.")` / `"Comment is too long."`. Use FluentValidation validators if the task-comment commands use them (check `CreateTaskComment`); otherwise validate in the handler the way `CreateTaskComment` does.
- Create and reply run in `_unitOfWork.ExecuteInTransactionAsync` and call:

```csharp
await _notifications.NotifyAsync(new WorkNotificationEvent(
    tenantId, subject.ProjectId, caller, WorkNotificationKinds.Commented, subject.ActionType, subject.TargetType,
    subject.TargetId, subject.TargetTitle, subject.ApprovalRequestId, subject.Participants), innerCt);
```

**Notification engine:**
- Add `public const string CommentedTemplate = "work_approval_commented";` and the switch arm `WorkNotificationKinds.Commented => CommentedTemplate`.
- Seeder entry (right after `work_approval_decided`):

```csharp
            new()
            {
                Id = Guid.NewGuid(), Code = "work_approval_commented",
                InAppTitleTemplate = "New comment on a request",
                InAppBodyTemplate = "{{actorName}} commented on {{actionLabel}} \"{{targetTitle}}\"."
            },
```

- `WorkActionLabels.For("module.invitation")` must return a sensible label. Check the class and add `"module.invitation" => "module invitation"` if missing.

**Controller routes** (in `WorkApprovalsController`):

```csharp
public sealed record CreateApprovalCommentRequest(string SubjectType, Guid SubjectId, string Content);
public sealed record ApprovalCommentContentRequest(string Content);

    [HttpGet("approval-comments")]
    public async Task<IActionResult> Comments([FromQuery] string subjectType, [FromQuery] Guid subjectId, CancellationToken ct)
        => ToResult(await _mediator.Send(new ListApprovalCommentsQuery(subjectType, subjectId), ct));

    [HttpPost("approval-comments")]
    public async Task<IActionResult> CreateComment([FromBody] CreateApprovalCommentRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new CreateApprovalCommentCommand(body.SubjectType, body.SubjectId, body.Content), ct));

    [HttpPost("approval-comments/{id:guid}/replies")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] ApprovalCommentContentRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new ReplyApprovalCommentCommand(id, body.Content), ct));

    [HttpPatch("approval-comments/{id:guid}")]
    public async Task<IActionResult> EditComment(Guid id, [FromBody] ApprovalCommentContentRequest body, CancellationToken ct)
        => ToResult(await _mediator.Send(new EditApprovalCommentCommand(id, body.Content), ct));
```

**Feed:** replace the temporary `CommentCount = 0` from Task 4 with `CountBySubjectsAsync` (two calls: one per subject type).

- [ ] **Step 1: Failing tests:**
  - **Create:**
    - the requester can comment on their own request → 1 notification to the approver;
    - a stranger → NotFound;
    - empty content → failure.
  - **Reply:**
    - a reply to a reply attaches to the top-level comment;
    - notifies the participants.
  - **Edit:**
    - the author edits → `IsEdited` true;
    - a non-author → Forbidden.
  - **List:** oldest first, `CanEdit` true only on the caller's own comments, author names resolved.
  - **Invitation subject:** the invitee and inviter can comment; others get NotFound.
  - **Feed:** `CommentCount` reflects saved comments (extend a Task 4 test).
  - **Seeder test:** 41 codes, includes `work_approval_commented`.
  - Use SQLite repo tests only if the Ef repository has non-trivial queries. `CountBySubjectsAsync` does, so add one repo test next to the existing `EfWorkApprovalRequestRepository` tests. Remember: a settable TestClock + separate save batches for ordering, and never write "SQLite" in `src/`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Unit + architecture suites green; Api Release build 0 errors.
- [ ] **Step 5: Commit**: `feat(work): comments on approval requests and invitations with notifications`

---

## Part C — Frontend

Before starting, open `docs/superpowers/plans/next/assets/2026-09-30-approvals-redesign-reference.webp` (backend repo) and keep it as the visual target.

Frontend `.ts` files are **CRLF**: use the Edit tool, or `\r?\n` in any perl.

### Task 7: DTOs, API service, feed utilities

**Files:**
- Create: `models/dto/approval-feed.dto.ts`, `utils/approval-feed.util.ts`, `utils/approval-feed.util.spec.ts`
- Modify: `data-access/work-approvals-api.service.ts`

**Interfaces:** Produces the types and functions below. Later tasks use exactly these names.

- [ ] **Step 1: DTOs**

```ts
import { WorkApprovalStatus } from './work-approval.dto';

export type ApprovalFeedSource = 'engine' | 'invitation';
export type ApprovalDirection = 'sent' | 'received';

export interface ApprovalFeedItemDto {
  id: string;
  source: ApprovalFeedSource;
  actionType: string;             // engine action types + 'module.invitation'
  targetType: 'task' | 'module' | 'sprint' | 'project';
  targetId: string | null;
  targetTitle: string;
  moduleId: string | null;
  moduleTitle: string | null;
  projectName: string;
  status: WorkApprovalStatus;
  direction: ApprovalDirection;
  requestedById: string;
  requestedByName: string;
  approverId: string;
  approverName: string;
  decidedById: string | null;
  decidedByName: string | null;
  decisionComment: string | null;
  createdAt: string;
  decidedAt: string | null;
  canDecide: boolean;
  canCancel: boolean;
  commentCount: number;
  summary: string | null;
}

export type ApprovalFieldKind = 'text' | 'longtext' | 'number' | 'hours' | 'date' | 'priority' | 'employee' | 'sprint';

export interface ApprovalFieldDto {
  key: string;
  label: string;
  kind: ApprovalFieldKind;
  current: string | null;
  requested: string | null;
  applied: string | null;
  changed: boolean;
  editable: boolean;
}

export interface ApprovalInvitationDto {
  inviteeId: string; inviteeName: string; inviteType: string;
  invitedById: string; invitedByName: string;
  moduleId: string; moduleTitle: string; expiresAt: string | null;
}

export interface ApprovalDetailDto {
  item: ApprovalFeedItemDto;
  requestedPayloadJson: string | null;
  appliedPayloadJson: string | null;
  fields: ApprovalFieldDto[];
  canEditPayload: boolean;
  note: string | null;
  currentAllocatedHours: number | null;
  invitation: ApprovalInvitationDto | null;
}

export interface ApprovalCommentDto {
  id: string;
  parentCommentId: string | null;
  authorId: string;
  authorName: string;
  content: string;
  isEdited: boolean;
  canEdit: boolean;
  createdAt: string;
  updatedAt: string | null;
}
```

- [ ] **Step 2: API methods.** Add them to `WorkApprovalsApiService`. Keep the existing approve/reject/cancel/createAllocationRequest/acceptObjectiveInvitation/rejectObjectiveInvitation methods.

```ts
  getApprovalFeed(projectId: string): Observable<ApprovalFeedItemDto[]> {
    return this.http.get<ApprovalFeedItemDto[]>(`${this.workBase}/projects/${projectId}/approval-feed`);
  }

  getApprovalDetail(id: string, source: ApprovalFeedSource): Observable<ApprovalDetailDto> {
    return this.http.get<ApprovalDetailDto>(`${this.workBase}/approvals/${id}`, { params: { source } });
  }

  getApprovalComments(subjectType: 'approval' | 'invitation', subjectId: string): Observable<ApprovalCommentDto[]> {
    return this.http.get<ApprovalCommentDto[]>(`${this.workBase}/approval-comments`, { params: { subjectType, subjectId } });
  }

  createApprovalComment(subjectType: 'approval' | 'invitation', subjectId: string, content: string): Observable<ApprovalCommentDto> {
    return this.http.post<ApprovalCommentDto>(`${this.workBase}/approval-comments`, { subjectType, subjectId, content });
  }

  replyApprovalComment(parentId: string, content: string): Observable<ApprovalCommentDto> {
    return this.http.post<ApprovalCommentDto>(`${this.workBase}/approval-comments/${parentId}/replies`, { content });
  }

  editApprovalComment(id: string, content: string): Observable<ApprovalCommentDto> {
    return this.http.patch<ApprovalCommentDto>(`${this.workBase}/approval-comments/${id}`, { content });
  }
```

`getApprovalHistory` and `getProjectApprovals` become unused after Task 11. Delete them there, together with their DTO files, if nothing else imports them.

- [ ] **Step 3: Failing util spec** (`approval-feed.util.spec.ts`):

```ts
import { APPROVAL_TYPE_META, approvalTypeKey, sortFeed, matchesApprovalFilters, buildApprovalFilterFields } from './approval-feed.util';

const base = { id: '1', source: 'engine', actionType: 'task.edit', targetType: 'task', targetId: 't', targetTitle: 'T',
  moduleId: 'm1', moduleTitle: 'M1', projectName: 'P', status: 'pending', direction: 'received',
  requestedById: 'e1', requestedByName: 'A', approverId: 'e2', approverName: 'B', decidedById: null, decidedByName: null,
  decisionComment: null, createdAt: '2026-09-29T10:00:00Z', decidedAt: null, canDecide: true, canCancel: false,
  commentCount: 0, summary: null } as const;

describe('approval-feed.util', () => {
  it('maps action types to type keys', () => {
    expect(approvalTypeKey('task.create')).toBe('task_creation');
    expect(approvalTypeKey('task.edit')).toBe('task_update');
    expect(approvalTypeKey('module.allocation_extend')).toBe('allocation');
    expect(approvalTypeKey('module.invitation')).toBe('invitation');
    expect(approvalTypeKey('module.edit')).toBe('module_update');
    expect(approvalTypeKey('sprint.start')).toBe('sprint');
    expect(approvalTypeKey('project.status_template_change')).toBe('status_template');
    expect(APPROVAL_TYPE_META['allocation'].label).toBe('Allocation');
  });

  it('sorts pending first, then newest first', () => {
    const rows = sortFeed([
      { ...base, id: 'old-decided', status: 'approved', createdAt: '2026-09-30T00:00:00Z' },
      { ...base, id: 'old-pending', createdAt: '2026-09-01T00:00:00Z' },
      { ...base, id: 'new-pending', createdAt: '2026-09-29T00:00:00Z' }
    ]);
    expect(rows.map(r => r.id)).toEqual(['new-pending', 'old-pending', 'old-decided']);
  });

  it('filters by type, status, direction, module, requester and date window', () => {
    const now = new Date('2026-09-30T12:00:00Z');
    expect(matchesApprovalFilters(base, { type: ['task_update'] }, now)).toBeTrue();
    expect(matchesApprovalFilters(base, { type: ['allocation'] }, now)).toBeFalse();
    expect(matchesApprovalFilters(base, { status: ['approved'] }, now)).toBeFalse();
    expect(matchesApprovalFilters(base, { direction: ['sent'] }, now)).toBeFalse();
    expect(matchesApprovalFilters(base, { direction: ['all'] }, now)).toBeTrue();
    expect(matchesApprovalFilters(base, { module: ['m1'] }, now)).toBeTrue();
    expect(matchesApprovalFilters(base, { requestedBy: ['e9'] }, now)).toBeFalse();
    expect(matchesApprovalFilters(base, { requestedOn: ['7'] }, now)).toBeTrue();
    expect(matchesApprovalFilters({ ...base, createdAt: '2026-08-01T00:00:00Z' }, { requestedOn: ['7'] }, now)).toBeFalse();
  });

  it('builds filter fields from the loaded rows only', () => {
    const fields = buildApprovalFilterFields([base]);
    expect(fields.map(f => f.key)).toEqual(['direction', 'type', 'status', 'module', 'requestedBy', 'requestedOn']);
    expect(fields.find(f => f.key === 'module')!.options).toEqual([{ value: 'm1', label: 'M1' }]);
  });
});
```

- [ ] **Step 4:** Run `npx ng test --watch=false --browsers=ChromeHeadless --include=src/app/modules/work/utils/approval-feed.util.spec.ts` → FAIL.

- [ ] **Step 5: Implement `approval-feed.util.ts`**

```ts
import { WorkDropdownOption } from '../ui/work-dropdown/work-dropdown.component';
import { TaskFilterFieldConfig } from '../ui/task-filter-bar/task-filter-bar.component';
import { ApprovalFeedItemDto } from '../models/dto/approval-feed.dto';

export type ApprovalTypeKey =
  | 'task_creation' | 'task_update' | 'task_deletion' | 'allocation' | 'module_update'
  | 'invitation' | 'sprint' | 'status_template';

/** tone picks a theme token; icon is an ApprovalTypeIconComponent name. */
export const APPROVAL_TYPE_META: Record<ApprovalTypeKey, { label: string; tone: 'accent' | 'success' | 'warning' | 'danger' | 'neutral'; icon: string }> = {
  task_creation:   { label: 'Task Creation',   tone: 'success', icon: 'task-plus' },
  task_update:     { label: 'Task Update',     tone: 'success', icon: 'task' },
  task_deletion:   { label: 'Task Deletion',   tone: 'danger',  icon: 'trash' },
  allocation:      { label: 'Allocation',      tone: 'accent',  icon: 'clock' },
  module_update:   { label: 'Module Update',   tone: 'accent',  icon: 'module' },
  invitation:      { label: 'Module Invitation', tone: 'warning', icon: 'user-plus' },
  sprint:          { label: 'Sprint',          tone: 'neutral', icon: 'sprint' },
  status_template: { label: 'Status Template', tone: 'neutral', icon: 'status' }
};

export function approvalTypeKey(actionType: string): ApprovalTypeKey {
  if (actionType === 'task.create') return 'task_creation';
  if (actionType === 'task.delete') return 'task_deletion';
  if (actionType.startsWith('task.')) return 'task_update';
  if (actionType === 'module.allocation_extend') return 'allocation';
  if (actionType === 'module.invitation') return 'invitation';
  if (actionType.startsWith('module.')) return 'module_update';
  if (actionType.startsWith('sprint.')) return 'sprint';
  return 'status_template';
}

const ACTION_TITLES: Record<string, string> = {
  'task.create': 'Add new task', 'task.edit': 'Update task details', 'task.delete': 'Delete task',
  'module.edit': 'Change module details', 'module.delete': 'Delete module', 'module.transfer': 'Transfer module owner',
  'module.achieve': 'Achieve module', 'module.unachieve': 'Reopen module', 'module.allocation_extend': 'Change allocated hours',
  'module.invitation': 'Add member to module',
  'sprint.create': 'Create sprint', 'sprint.edit': 'Update sprint', 'sprint.start': 'Start sprint',
  'sprint.complete': 'Complete sprint', 'sprint.achieve': 'Achieve sprint', 'sprint.delete': 'Delete sprint',
  'project.status_template_change': 'Change task statuses'
};

export function approvalTitle(item: ApprovalFeedItemDto): string {
  return ACTION_TITLES[item.actionType] ?? 'Request';
}

/** "Update task details" row subtitle: the target plus the summary line when there is one. */
export function approvalSubtitle(item: ApprovalFeedItemDto): string {
  return item.summary ? `${item.targetTitle} · ${item.summary}` : item.targetTitle;
}

export function statusLabel(item: ApprovalFeedItemDto): string {
  const invitation = item.source === 'invitation';
  switch (item.status) {
    case 'pending': return 'Pending';
    case 'approved': return invitation ? 'Accepted' : 'Approved';
    case 'rejected': return invitation ? 'Declined' : 'Rejected';
    case 'cancelled': return 'Cancelled';
    default: return invitation ? 'Expired' : 'Outdated';
  }
}

export function sortFeed(items: readonly ApprovalFeedItemDto[]): ApprovalFeedItemDto[] {
  return [...items].sort((a, b) => {
    const pa = a.status === 'pending' ? 0 : 1;
    const pb = b.status === 'pending' ? 0 : 1;
    return pa !== pb ? pa - pb : Date.parse(b.createdAt) - Date.parse(a.createdAt);
  });
}

export type ApprovalFilterValues = Partial<Record<'direction' | 'type' | 'status' | 'module' | 'requestedBy' | 'requestedOn', readonly string[]>>;

export function matchesApprovalFilters(item: ApprovalFeedItemDto, f: ApprovalFilterValues, now = new Date()): boolean {
  const has = (vals: readonly string[] | undefined, v: string | null) => !vals?.length || (v !== null && vals.includes(v));
  const direction = f.direction?.[0];
  if (direction && direction !== 'all' && item.direction !== direction) return false;
  if (!has(f.type, approvalTypeKey(item.actionType))) return false;
  if (!has(f.status, item.status)) return false;
  if (!has(f.module, item.moduleId)) return false;
  if (!has(f.requestedBy, item.requestedById)) return false;
  const days = Number(f.requestedOn?.[0]);
  if (days > 0 && now.getTime() - Date.parse(item.createdAt) > days * 86_400_000) return false;
  return true;
}

function distinct(items: readonly ApprovalFeedItemDto[], key: (i: ApprovalFeedItemDto) => [string | null, string | null]): WorkDropdownOption[] {
  const map = new Map<string, string>();
  for (const i of items) { const [v, l] = key(i); if (v && l && !map.has(v)) map.set(v, l); }
  return [...map].map(([value, label]) => ({ value, label })).sort((a, b) => a.label.localeCompare(b.label));
}

export function buildApprovalFilterFields(items: readonly ApprovalFeedItemDto[]): TaskFilterFieldConfig[] {
  const typeKeys = [...new Set(items.map(i => approvalTypeKey(i.actionType)))];
  return [
    { key: 'direction', label: 'Requests', icon: 'type', kind: 'single', defaultValues: ['all'],
      options: [{ value: 'all', label: 'All requests' }, { value: 'received', label: 'Received by me' }, { value: 'sent', label: 'Sent by me' }] },
    { key: 'type', label: 'Type', icon: 'type', kind: 'multi',
      options: typeKeys.map(k => ({ value: k, label: APPROVAL_TYPE_META[k].label })) },
    { key: 'status', label: 'Status', icon: 'status', kind: 'multi',
      options: [{ value: 'pending', label: 'Pending' }, { value: 'approved', label: 'Approved' }, { value: 'rejected', label: 'Rejected' },
                { value: 'cancelled', label: 'Cancelled' }, { value: 'stale', label: 'Outdated' }] },
    { key: 'module', label: 'Module', icon: 'module', kind: 'multi', options: distinct(items, i => [i.moduleId, i.moduleTitle]) },
    { key: 'requestedBy', label: 'Requested by', icon: 'people', kind: 'multi', options: distinct(items, i => [i.requestedById, i.requestedByName]) },
    { key: 'requestedOn', label: 'Requested on', icon: 'calendar', kind: 'single', defaultValues: ['any'],
      options: [{ value: 'any', label: 'Any time' }, { value: '7', label: 'Last 7 days' }, { value: '30', label: 'Last 30 days' }, { value: '90', label: 'Last 90 days' }] }
  ];
}
```

Check `TaskFilterFieldConfig` and `WorkDropdownOption` against the real types. If `single` fields need a different default shape, adapt it and note the deviation.

- [ ] **Step 6:** Spec → PASS. **Commit**: `feat(work): approval feed DTOs, API and filter/sort utilities`

### Task 8: Store rewrite

**Files:**
- Rewrite: `state/work-approvals.store.ts` (+ new `state/work-approvals.store.spec.ts`; replace the old spec if one exists)
- Modify: `feature/project-detail/project-detail.component.ts` (and its spec)

**Interfaces:**
- **State:**
  - `items: ApprovalFeedItemDto[]` (sorted), `loading`, `error`, `projectId`;
  - `selectedId: string | null`, `detail: ApprovalDetailDto | null`, `detailLoading`, `detailError`;
  - `comments: ApprovalCommentDto[]`, `commentsLoading`;
  - `busy: boolean` (a decision is in flight).
- **Computed:**
  - `pendingCount` = items with `direction === 'received' && canDecide`. This is the red dot on the Approvals tab.
  - `selected` = item by `selectedId`.
- **Methods:**
  - `loadAll(projectId)`;
  - `select(item | null)`: loads the detail + comments; `null` clears them;
  - `approve(item, editedPayloadJson?)`: engine → `approveWorkApproval`; invitation → `acceptObjectiveInvitation`;
  - `reject(item, reason?)`: engine → `rejectWorkApproval(id, reason || undefined)`; invitation → `rejectObjectiveInvitation`;
  - `cancel(item)`: `cancelWorkApproval`;
  - `addComment(content)`, `reply(parentId, content)`, `editComment(id, content)`.
  - Every decision reloads the feed (so the row moves down into history) and re-selects the same id so the card shows the decided state.
  - Every comment mutation reloads the comments and bumps `commentCount` locally.
  - All methods return `Promise<boolean>` and set `error` / `detailError` with `extractError`, keeping the existing helper.

- [ ] **Step 1: Failing spec** with a jasmine spy object for `WorkApprovalsApiService` (see how other store specs in `state/` provide mocks). Cases:
  - `loadAll` sorts pending first and computes `pendingCount`;
  - `select` loads the detail with the item's `source` and loads comments with `subjectType` `invitation` for invitation rows;
  - `approve` on an engine row passes `editedPayloadJson` through, then reloads;
  - `approve` on an invitation calls `acceptObjectiveInvitation`;
  - `reject` without a reason calls `rejectWorkApproval(id, undefined)`;
  - `addComment` posts, then reloads comments.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement with `signalStore` in the same style as the current file. **Step 4:** PASS.
- [ ] **Step 5:** `project-detail.component.ts`: replace `pendingApprovalCount` with `computed(() => this.approvalsStore.pendingCount())` and drop the now-unused imports and filters. Update its spec expectations. Run `ng build` → it may fail only in `work-approvals.component.ts`, which Task 11 rewrites. To keep the commit building, do Task 8's commit **together with Task 11** if the build breaks (note it in the commit message, as Plan 2 did).
- [ ] **Step 6: Commit** (or defer, per Step 5): `feat(work): approvals store on the unified feed`

### Task 9: Table + type icon components

**Files:**
- Create: `ui/approval-type-icon/approval-type-icon.component.ts`, `ui/approval-table/approval-table.component.ts` + spec

**Interfaces:**
- `ApprovalTypeIconComponent`: `selector: 'app-approval-type-icon'`, `icon = input.required<string>()`, `tone = input<'accent'|'success'|'warning'|'danger'|'neutral'>('accent')`. Renders a 32px rounded tile with a tinted background (`color-mix(in srgb, var(--tone) 14%, transparent)`) and an inline SVG for each icon: `task-plus`, `task`, `trash`, `clock`, `module`, `user-plus`, `sprint`, `status`.
- Tone → token: accent `--color-accent`, success `--color-success`, warning `--color-warning`, danger `--color-danger`, neutral `--color-text-secondary`.
- `ApprovalTableComponent` (`app-approval-table`):
  - Inputs: `items: ApprovalFeedItemDto[]` (already filtered + sorted), `selectedId: string | null`, `busyId: string | null`.
  - Outputs:
    - `rowSelected: ApprovalFeedItemDto`;
    - `quickApprove: ApprovalFeedItemDto`;
    - `cancelRequest: ApprovalFeedItemDto`;
    - `openTarget: ApprovalFeedItemDto`;
    - `copyLink: ApprovalFeedItemDto`.
  - Internal paging: `pageSize` signal (10 default; 10, 20 and 50 options through `app-work-dropdown`) and `page` signal. Reset to page 1 whenever `items` changes, using an `effect` on the items input.
  - Columns exactly as in the Design section.
  - Status pill tones: pending → warning; approved → success; rejected → danger; cancelled/stale → neutral.
  - Type pill uses `APPROVAL_TYPE_META` tone.
  - Avatar: reuse `app-employee-avatar` (check its inputs: name/id).
  - Dates: `DatePipe` `'MMM d, y'` + `'h:mm a'` on the second line.
  - The comment count shows as a small speech-bubble icon + number next to the title when > 0.
- **3-dot menu:** reuse `app-dropdown-menu` from `shared/ui/dropdown-menu` (inputs `items: MenuItem[]`, output `selected: string`; check the `MenuItem` shape). Build items per row:

```ts
protected menuFor(item: ApprovalFeedItemDto): MenuItem[] {
  const items: MenuItem[] = [{ id: 'view', label: 'View details' }];
  if (item.status === 'pending' && item.canDecide)
    items.push({ id: 'approve', label: item.source === 'invitation' ? 'Accept' : 'Approve' });
  if (item.targetId && (item.targetType === 'task' || item.targetType === 'module'))
    items.push({ id: 'open', label: item.targetType === 'task' ? 'Open task' : 'Open module' });
  if (item.status === 'pending' && item.canCancel) items.push({ id: 'cancel', label: 'Cancel request' });
  items.push({ id: 'copy', label: 'Copy link' });
  return items; // never 'reject' — rejecting always goes through the card's reminder dialog
}
```

  Clicks inside the menu must not select the row: use `$event.stopPropagation()` on the menu host.
- **Empty state:** when `items` is empty, show a centred icon + "No requests match these filters." (the page passes a different message when there are no requests at all; add an `emptyMessage` input, default as above).

- [ ] **Step 1: Failing spec:**
  - renders one row per item (up to page size) and "Showing 1 - 10 of 12 requests";
  - clicking a row emits `rowSelected`;
  - the selected row has class `approval-table__row--selected`;
  - the 3-dot menu for a pending decidable row contains Approve and **never** an item with id or label "Reject";
  - an invitation row shows "Accept";
  - next page shows rows 11–12;
  - a status pill shows "Accepted" for an approved invitation.
- [ ] **Step 2:** FAIL → **Step 3:** implement (theme tokens only) → **Step 4:** PASS.
- [ ] **Step 5: Commit**: `feat(work): approvals table with paging and quick actions`

### Task 10: Explanation card, comments, reject dialog

**Files:**
- Create: `ui/approval-explanation-card/approval-explanation-card.component.ts` + spec, `ui/approval-comments/approval-comments.component.ts` + spec, `ui/approval-reject-dialog/approval-reject-dialog.component.ts` + spec

**Interfaces:**

**`ApprovalRejectDialogComponent`** (`app-approval-reject-dialog`)
- Inputs: `open: boolean`, `title: string` (e.g. "Reject request?" / "Decline invitation?").
- Outputs: `confirmed: string | null` (reason or null), `closed: void`.
- UI: `app-modal`, then this text: "Adding a reason helps the requester understand the decision. It's optional."
- A textarea (placeholder "Reason (optional)").
- Buttons:
  - `Cancel` (secondary);
  - `Reject without reason` (secondary, emits null) — shown only while the textarea is empty;
  - `Reject` (danger), which emits the trimmed text, or null when empty.

**`ApprovalCommentsComponent`** (`app-approval-comments`)
- Inputs: `comments: ApprovalCommentDto[]`, `loading: boolean`.
- Outputs: `add: string`, `reply: { parentId: string; content: string }`, `edit: { id: string; content: string }`.
- Groups replies under their top-level comment (by `parentCommentId`).
- Each comment shows: avatar, author, relative time (reuse whatever pipe or helper `task-comments.component.ts` uses), content, "(edited)" when `isEdited`, `Reply`, and `Edit` only when `canEdit`.
- Edit swaps the text for a textarea with Save/Cancel.
- Composer at the bottom: textarea + `Comment` button, disabled while empty.
- Reuse `app-comment-composer` **only if** its inputs/outputs are generic (plain text in, content out, no task id). Otherwise use a plain textarea styled like it. Don't modify `comment-composer` itself; task comments depend on it.

**`ApprovalExplanationCardComponent`** (`app-approval-explanation-card`)
- Inputs: `detail: ApprovalDetailDto | null`, `loading: boolean`, `error: string | null`, `comments: ApprovalCommentDto[]`, `commentsLoading: boolean`, `busy: boolean`.
- Outputs:
  - `closed: void`;
  - `approve: string | undefined` (editedPayloadJson);
  - `reject: string | null` (reason);
  - `cancelRequest: void`;
  - `openTarget: void`;
  - `addComment: string`;
  - `replyComment: {parentId, content}`;
  - `editComment: {id, content}`.
- Sections as in the Design section.
- **Changes table:** the last column header is "Approve value" while `canEditPayload`, "Approved" when `appliedPayloadJson` exists, otherwise hidden.
- **Editable inputs:**
  - text → `<input>`;
  - number/hours → `<input type="number">` with an `h` suffix for hours;
  - date → `app-date-picker` if its API is a simple value in/out (check it), else `<input type="date">`;
  - priority → `app-work-dropdown` with Low/Medium/High/Urgent. Use the real priority values from the task model (`grep -rn "Priority" models/task.model.ts`).
- Edited values live in `edits = signal<Record<string, string>>({})`. Reset it whenever `detail()?.item.id` changes (effect).
- **Allocation rows:** above the table show "Current allocation {{currentAllocatedHours}}h → {{current + requested}}h after approval". When edited, the "after approval" value updates live.
- **Invitation block** for `detail.invitation`.
- **Activity timeline**, built by a pure exported function `buildTimeline(detail): {icon, text, at}[]`:
  - Requested by X (`createdAt`);
  - then, when decided: "Approved by Y" / "Approved with changes by Y" (`appliedPayloadJson` non-null) / "Rejected by Y — reason" / "Cancelled by requester" / "Closed as outdated", at `decidedAt`.
  - The comment thread renders under it.
- **Footer (sticky):**
  - `detail.item.canDecide`:
    - engine → `Reject` + `Approve`;
    - invitation → `Decline` + `Accept`.
  - `detail.item.canCancel` → `Cancel request`.
  - Reject/Decline opens `app-approval-reject-dialog`; its `confirmed` emits `reject`.
- **Approve:** when `edits()` is non-empty, build the edited payload and emit it; otherwise emit undefined.

```ts
/** Overwrites the edited keys on the requested payload. Keys are the payload's own property names. */
export function buildEditedPayload(requestedJson: string | null, fields: readonly ApprovalFieldDto[], edits: Record<string, string>): string | undefined {
  const keys = Object.keys(edits);
  if (keys.length === 0) return undefined;
  const payload = requestedJson ? JSON.parse(requestedJson) as Record<string, unknown> : {};
  for (const key of keys) {
    const field = fields.find(f => f.key === key);
    if (!field) continue;
    const raw = edits[key].trim();
    payload[key] = raw === '' ? null
      : field.kind === 'number' || field.kind === 'hours' ? Number(raw)
      : raw;
  }
  return JSON.stringify(payload);
}
```

- A `Approve` click with an invalid number (NaN or ≤ 0 for hours) shows an inline error under the field and does not emit.

- [ ] **Step 1: Failing specs:**
  - `buildEditedPayload` keeps PascalCase keys (`{"Title":"A","Priority":"High"}` + edit Title=B → `{"Title":"B","Priority":"High"}`) and converts hours to a number.
  - `buildTimeline` for a rejected-with-reason detail ends with "Rejected by B — too big".
  - Card:
    - shows the Current/Requested columns and highlights changed rows (class `approval-card__row--changed`);
    - editable inputs only when `canEditPayload`;
    - Approve emits the edited payload;
    - Reject opens the dialog, and "Reject without reason" emits `null`;
    - a requester's pending row shows only `Cancel request`;
    - an invitation shows the invitation block and Accept/Decline.
  - Comments: replies nest under the parent; Edit only on `canEdit`; the composer emits trimmed content and is disabled while empty.
  - Reject dialog: the button label and emitted value with and without text.
- [ ] **Step 2:** FAIL → **Step 3:** implement (theme tokens + inline SVG icons only) → **Step 4:** PASS.
- [ ] **Step 5: Commit**: `feat(work): approval explanation card with editable changes, timeline and comments`

### Task 11: Page rewrite + cleanup

**Files:**
- Rewrite: `feature/work-approvals/work-approvals.component.ts` + spec
- Delete (after confirming no other importer with grep):
  - `ui/approval-request-row/*`;
  - `ui/approval-history-list/*`;
  - `ui/task-status-change-requests-panel/*`;
  - `utils/approval.mapper.ts`, `models/approval.model.ts`, `models/dto/approval-history.dto.ts`;
  - the now-unused API methods (`getApprovalHistory`, `getProjectApprovals`, `getMyObjectiveInvitations` if unused).
- Also delete the Angular task-status-change-requests API methods **only** if the settings page doesn't use them (grep first).

**Interfaces:** Consumes the Task 7–10 components and store.

Page behaviour:
- **Toolbar template** (set via `WorkPageToolbarStore` exactly as today): `app-task-filter-bar` with `[fields]="filterFields()" [values]="filterValues()" (fieldChanged)="onFilterChanged($event)"` and `Create Approval`.
  - Keep the existing Create Approval modal code as is: fields, validation, `createAllocationRequest`.
  - After a successful create, call `store.loadAll(projectId)`.
- **Filter state:**
  - `filterValues = signal<ApprovalFilterValues>({ direction: ['all'], requestedOn: ['any'] })`;
  - `filterFields = computed(() => buildApprovalFilterFields(store.items()))`;
  - `visible = computed(() => store.items().filter(i => matchesApprovalFilters(i, filterValues())))`.
  - Check the `fieldChanged` payload shape in `task-backlog.component.ts#onFilterFieldChanged` and mirror it.
- **Layout:**
  - `h1` "Approvals" + subtitle;
  - a grid `minmax(0,1fr) 480px` when a row is selected, otherwise a single column;
  - below 1100px the card is a fixed right drawer (full height, `z-index` above the table) with a scrim.
- **Row select:** `store.select(item)` + `router.navigate([], { queryParams: { request: item.id }, queryParamsHandling: 'merge', replaceUrl: true })`. Closing the card clears the param.
- **Deep link:** after the first `loadAll`, if `?request=` matches an item, select it.
- **Quick actions:**
  - `quickApprove` → `store.approve(item)` + toast "Approved";
  - `cancelRequest` → `store.cancel(item)`;
  - `openTarget`: task → open `app-task-form-modal` in edit mode exactly as the current page does (`selectedTaskId`); module → `router.navigate(['/work', projectId, 'milestones', targetId, 'tree'])`;
  - `copyLink` → `navigator.clipboard.writeText(location.origin + '/work/' + projectId + '/approvals?request=' + id)` + toast "Link copied".
  - Use the app's existing toast service (see how `SprintListStore` shows "Sent for approval.").
- **Loading/error:** a skeleton or a "Loading approvals…" line while `store.loading()`. The error state has a retry that calls `loadAll`. If there are no requests at all: "No approval requests in this project yet."
- **Project switch:** keep the existing `paramMap` subscription pattern. On a new project id, clear the selection and call `store.loadAll(id)`. The project-detail shell already calls `loadAll` for the badge, so don't double load: follow the current comment and rely on the shell's call unless the store's `projectId` differs.

- [ ] **Step 1: Rewrite the page spec:**
  - the toolbar registers a template;
  - the table receives sorted, filtered items;
  - changing the direction filter to `sent` hides received rows;
  - selecting a row loads the detail and sets `?request=`;
  - `?request=<id>` on load selects that item;
  - the quick-approve calls `store.approve` without an edited payload;
  - the card's `reject` with null calls `store.reject(item, undefined)`;
  - the Create Approval modal still validates and submits (port the existing tests for it).
- [ ] **Step 2:** FAIL → **Step 3:** implement → **Step 4:** PASS.
- [ ] **Step 5: Delete** the unused files listed above (grep each one first) and delete their specs. Then run `npx ng build` → green, and run the full `ng test` → green apart from the known flaky specs (re-run a failing one in isolation to prove it passes).
- [ ] **Step 6: Commit**: `feat(work): single-list Approvals page with filters and explanation card`. List every deleted component/spec file in the message body.

### Task 12: Final gate + handoff

- [ ] **Backend:** `dotnet build src/ONEVO.Api -c Release` (0 errors), unit + architecture suites green (record counts). Also `dotnet ef migrations has-pending-model-changes ...` → clean.
- [ ] **Frontend:** `npx ng build` green, `npx ng test --watch=false --browsers=ChromeHeadless` (record pass/fail counts; list flaky ones proven green in isolation).
- [ ] **Scope check:**
  - `git diff --stat feature/wm-hierarchy-approval-notification-engine...HEAD` in both repos;
  - only WM files plus `NotificationTemplateSeeder.cs` and its test.
- [ ] **Colour check:** `grep -nE "#[0-9a-fA-F]{3,6}\b" src/app/modules/work/ui/approval-* src/app/modules/work/feature/work-approvals/*.ts` → no matches except inside SVG `stroke="currentColor"`-free code (there should be none).
- [ ] **Report to the user:**
  - **Commits:** list every commit hash.
  - **Migration to apply:** `AddApprovalCommentsAndAppliedPayload`.
  - **Manual browser checklist:**
    1. Approver: open a pending task edit, change the due date, approve → the task has the edited date; the card shows Requested vs Approved.
    2. Reject with no reason → the reminder dialog → "Reject without reason" works.
    3. Requester: sees their request with status; after the decision it stays in the list (history).
    4. Comment as requester, reply as approver, edit own comment; the other side gets a bell notification.
    5. An invitation shows invitee/type/module; the invitee can Accept/Decline from the card.
    6. The 3-dot menu never shows Reject.
    7. Filters: direction, type, status, module, requester, date window.
    8. Mobile width: the card becomes a drawer.
    9. Dark mode + a different accent colour.

---

## Self-review notes

- Every user requirement maps to a task:
  - one list + pending on top → T4/T7/T11;
  - filters with the existing component → T7/T11;
  - explanation card, before/after and invite details → T5/T10;
  - approver edits then applies → T2/T5/T10;
  - sent + received + status + history → T3/T4;
  - comments create/reply/edit own → T6/T10;
  - 3-dot without reject → T9;
  - optional reject reason with a reminder → T10;
  - theme colours + icons → Global Constraints / T9 / T10 / T12.
- Names used across tasks:
  - `ApprovalFeedItemResponse` ↔ `ApprovalFeedItemDto` (camelCase JSON of the same fields);
  - `ApprovalFieldResponse` ↔ `ApprovalFieldDto`;
  - `approvalTypeKey`, `sortFeed`, `matchesApprovalFilters`, `buildApprovalFilterFields`, `buildEditedPayload`, `buildTimeline`.
- Known judgement calls the executor may hit:
  - exact entity property names for sprint and task current values;
  - `app-date-picker` and `app-comment-composer` API fit;
  - `fieldChanged` payload shape.

  Resolve each minimally and record it in the commit message. If a change would alter behaviour or a user decision (D1–D11), stop and ask.
