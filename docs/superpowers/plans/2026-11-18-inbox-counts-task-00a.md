# Inbox Counts Task 00a Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `GET /api/v1/inbox/counts` compute badge totals without building the inbox list and reach under 200 ms for the dapi owner dataset.

**Architecture:** Add a count-only contract to each inbox source. The router calls those providers sequentially on the shared DbContext, aggregates pending received requests and open alerts by source/kind, then reads chat unread count once. Simple sources use SQL `COUNT`/grouped-count repository queries; policy-routed sources load only candidate identifiers required by authority/routing rules and never construct `InboxItem`, load display data, or inspect sent/history rows.

**Tech Stack:** .NET 10, C#, EF Core 10, SQL Server, xUnit, Moq/FluentAssertions.

**Spec:** `docs/Improvements/person-2-performance-and-db/task-00a Inbox counts without loading the whole inbox.md`

## Global Constraints

- Count visibility and pending rules must exactly match each provider's `ListAsync` output for the same caller.
- Providers execute sequentially; the request-scoped DbContext is not safe for parallel use.
- Do not add a count cache: DEC-053 explicitly rejects stale inbox badges until every module emits invalidation.
- Count paths must not construct `InboxItem`, load display names, actions, filter fields, sent items, decided history, or chat-added informational items.
- Preserve the existing `InboxCounts` JSON contract.
- Tenant isolation remains enforced by the existing tenant-aware repositories and SQL Server RLS.

## Review Focus

- A caller who requested their own item must not have that sent item counted as pending received.
- Monitoring/calendar alerts visible as the subject's own informational item still count as open alerts, matching the list-derived count.
- Routed alerts and approvals count only for the exact current holder/approver; broad HR or manager visibility alone must not add a badge.
- Duplicate visibility paths for the same item count once.
- A provider returning no rows must still contribute an explicit zero without suppressing other providers or unread chat totals.

---

### Task 1: Count contract and router aggregation

**Files:**
- Modify: `src/ONEVO.Application/Features/Inbox/Contracts/InboxContracts.cs`
- Modify: `src/ONEVO.Application/Features/Inbox/Services/InboxRouter.cs`
- Modify: `tests/ONEVO.Tests.Unit/Features/Inbox/InboxRouterTests.cs`
- Modify: test-only `IInboxSourceProvider` fakes/stubs found by the compiler

**Interfaces:**
- Produces: `InboxSourceCounts(int PendingReceived, int OpenAlerts, IReadOnlyDictionary<string,int> ByKind)`
- Produces: `Task<InboxSourceCounts> IInboxSourceProvider.CountAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)`

- [ ] **Step 1: Write failing router tests** proving counts come from `CountAsync`, providers are called sequentially, source/kind totals aggregate, unread chat count is preserved, and `ListAsync` is never called.
- [ ] **Step 2: Run `InboxRouterTests` and verify RED** because the count contract does not exist.
- [ ] **Step 3: Add the contract and replace `InboxRouter.GetCountsAsync` list collection with sequential count aggregation.** `BySource` uses each provider's `Source`; `ByKind` keys remain `source:kind`.
- [ ] **Step 4: Update compile-only test doubles with explicit count results and run `InboxRouterTests` GREEN.**
- [ ] **Step 5: Commit the contract/router slice.**

### Task 2: Request-source count paths (Onboarding and Leave)

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/Inbox/OnboardingInboxSourceProvider.cs`
- Modify: `src/ONEVO.Application/Features/CoreHr/Onboarding/RepositoryInterfaces/IOnboardingInboxRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/CoreHr/EfOnboardingInboxRepository.cs`
- Modify: `src/ONEVO.Application/Features/Leave/Inbox/LeaveInboxSourceProvider.cs`
- Modify: leave approval repository interfaces/EF implementations selected by the failing parity test
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/OnboardingInboxSourceProviderTests.cs`
- Test: existing/new `LeaveInboxSourceProviderTests.cs`

**Interfaces:**
- Consumes: `InboxSourceCounts` and `IInboxSourceProvider.CountAsync`
- Produces: grouped pending-received counts for onboarding kinds and leave requests

- [ ] **Step 1: Add parity tests** deriving literal expected totals from controlled pending/sent/decided fixtures; include a pending item requested by the caller and a caller without approve permission.
- [ ] **Step 2: Run the two provider test groups and verify RED.**
- [ ] **Step 3: Add repository grouped-count queries and provider `CountAsync` implementations.** Reuse the same permission, actionable, delegation and in-order gates as the list path; do not load history, own/sent requests, people or conversations.
- [ ] **Step 4: Run provider parity tests GREEN.**
- [ ] **Step 5: Commit the request-source slice.**

### Task 3: Monitoring and Calendar alert count paths

**Files:**
- Modify: `src/ONEVO.Application/Features/Monitoring/Exceptions/Inbox/ExceptionInboxSourceProvider.cs`
- Modify: `src/ONEVO.Application/Features/Calendar/Inbox/CalendarMeetingExceptionInbox.cs`
- Modify: exception repository interfaces/EF implementation only where a lightweight active-candidate projection is missing
- Test: `tests/ONEVO.Tests.Unit/Features/Monitoring/Exceptions/ExceptionInboxTests.cs`
- Test: existing calendar inbox provider tests

**Interfaces:**
- Produces: open-alert counts by monitoring/calendar kind after exact routing

- [ ] **Step 1: Add parity tests** for own cases, routed cases, broad-scope-but-not-routed cases, chat-only info, calendar/non-calendar separation and duplicate visibility.
- [ ] **Step 2: Run tests and verify RED.**
- [ ] **Step 3: Implement count-only candidate reads and routing.** Count own plus routed active cases distinctly; exclude chat-added informational rows that are not open/list-pending and never load people/evidence/actions.
- [ ] **Step 4: Run provider tests GREEN.**
- [ ] **Step 5: Commit the alert-source slice.**

### Task 4: Attendance count path

**Files:**
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Inbox/AttendanceInboxSourceProvider.cs`
- Modify: `src/ONEVO.Application/Features/TimeAttendance/Inbox/AttendanceAlertInbox.cs`
- Modify: attendance request/alert repository interfaces and EF implementations needed for grouped counts
- Test: existing attendance inbox tests (add a focused count test file if clearer)

**Interfaces:**
- Produces: pending received totals for correction/work-mode/location/device requests and open alert totals by kind

- [ ] **Step 1: Add parity tests** for every request kind, exact approval scope, unscoped device requests resolved through legal entity, own alerts and received alerts.
- [ ] **Step 2: Run tests and verify RED.**
- [ ] **Step 3: Implement count-only request and alert paths.** Reuse `ResolveApprovalInboxScopeAsync`; query only scoped pending counts/ids and skip sent rows, display people and conversation subjects.
- [ ] **Step 4: Run attendance tests GREEN.**
- [ ] **Step 5: Commit the attendance slice.**

### Task 5: Work Management count path

**Files:**
- Modify: `src/ONEVO.Application/Features/WorkManagement/Inbox/WorkInboxSourceProvider.cs`
- Modify: approval feed/request and project invitation repository interfaces/EF implementations
- Modify: monitor-alert repository/audience helpers only where a lightweight open-count projection is required
- Test: `tests/ONEVO.Tests.Unit/Features/WorkManagement/Inbox/WorkInboxSourceProviderTests.cs`

**Interfaces:**
- Produces: pending received approval/invitation totals and open monitor-alert totals by kind

- [ ] **Step 1: Add parity tests** for pending approvals, invitations, sent requests, resolved alerts, audience duplicates and projects the caller can only see through an invitation.
- [ ] **Step 2: Run tests and verify RED.**
- [ ] **Step 3: Implement count-only queries.** Do not call `WorkApprovalFeedService.BuildAsync`, `ProjectMonitorSnapshotLoader`, alert-context mapping, display-name resolution or conversation detail reads.
- [ ] **Step 4: Run Work inbox and hierarchy/audience tests GREEN.**
- [ ] **Step 5: Commit the Work slice.**

### Task 6: SQL command guard, full verification and performance evidence

**Files:**
- Add/modify: SQL Server integration test under `tests/ONEVO.Tests.Integration/Inbox/`
- Modify: `docs/reports/performance/2026-11-15-api-response-timings.md` or add a dated Task 00a follow-up report

**Interfaces:**
- Consumes: all provider count implementations
- Produces: regression guard showing `/inbox/counts` does not execute list/detail queries and measured before/after latency/query count

- [ ] **Step 1: Add an integration test** that seeds representative rows across sources, compares endpoint counts with list-derived totals, and captures SQL command count.
- [ ] **Step 2: Temporarily route the endpoint back through `CollectAsync` and verify the guard fails; restore the implementation and verify GREEN.**
- [ ] **Step 3: Run focused unit tests, architecture tests, and routed SQL Server integration tests.**
- [ ] **Step 4: Measure warm dapi-owner calls, record median latency and SQL command count, and require `<200 ms`; if the target is missed, profile the remaining commands before changing code.**
- [ ] **Step 5: Run the full unit suite and Release build, inspect the diff, and commit verification/report changes.**

