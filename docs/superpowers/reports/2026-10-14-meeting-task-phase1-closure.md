# Meetings as Tasks — Phase 1 closure pass (2026-10-14)

Branch `feature/meeting-task-phase0-1` (backend and frontend). Spec: `docs/superpowers/specs/2026-10-13-meeting-task-phase0-1-design.md`, verified against the **approved** text at `3df349ee`. Plan: `docs/superpowers/plans/2026-10-13-meeting-task-phase1-foundations.md`.

## 1. Spec vs. implementation

| Area (approved spec) | Finding before this pass | Result |
|---|---|---|
| §4.2 "After an employee is created: onboarding finalize and `ApproveAccessGrantRequestCommandHandler` call the reconciler after their save" | **Defect.** The plan replaced this with a call inside `EnsureOfficeProjectAsync`. That call runs only when there are checklist tasks, and *before* the new employee is saved, so it never sees the employee being created. | Fixed in `c5791247`. Both paths call `TryReconcileAsync` after commit. It is best-effort and never fails onboarding. The spec's own amendment was reverted to the approved wording. |
| §4.2 step 2 "adds the owner as a system ProjectMember … if they aren't one" | **Defect.** The membership lookup went through the soft-delete query filter, but the unique index is not filtered by `is_deleted`. A soft-deleted owner row caused a unique violation that was logged as "added concurrently", and the owner stayed out of the project. | Fixed in `84d1432e`. The soft-deleted row is restored instead of re-added. The architecture allowlist was updated in `5a7e18d8`. |
| §4.2 step 3 "retries the next time it runs" / Concurrency "concurrent runs converge" (failed-reconciliation recovery) | **Defects.** (a) A create race lost to an HR-led Ensure was reported as `Created`, with HR still leading. (b) An eligible owner whose employee had no resolvable company ended the search, so the next owner was never tried. (c) A failed startup pass, or a tenant that threw during it, was not retried until the next restart. | Fixed in `31fa894c`. (a) The reconciler falls through to the align path. (b) It continues to the next owner. (c) The startup run makes up to 3 attempts; later attempts retry only the failed tenants. A partially applied earlier run now has a test proving it converges (regression guard; it passed without changes). |
| §4.2 "Startup backfill … safe to run on every start" (deterministic pagination) | **Defect.** Offset paging over `created_at DESC` with no tie-breaker. A tenant created mid-run shifts the pages, so a tenant is reconciled twice and another can be skipped. Equal timestamps were also nondeterministic. | Fixed in `e39a968e`. Keyset paging by `id` uses the new `ITenantRepository.ListPageByIdAsync`. Every tenant that existed when the run started is visited exactly once. |

Phase 1 commits from before this pass (for reference): `24970ebf`, `6e00cf0d`, `80d57272`, `0787cbd8`, `4f6040e3`, `1e8c2703`, `ce873286`, `77dcd9d1`, `0acc12d1`, `1ef27e70`, `83e8d5a8`, `c1448cf5`. Frontend: `d2ffeaed`.

## 2. Regression tests (each watched fail, then pass)

| Defect | Test | RED (before fix) |
|---|---|---|
| Employee-creation trigger, onboarding approval | `ApproveAccessGrantRequestCommandHandlerTests.Handle_SeatApproved_ReconcilesTheOfficeProjectAfterTheEmployeeIsSaved` | expected `[save, reconcile]`, actual `[save]` |
| Employee-creation trigger, finalize | `OnboardingDraftWriteServiceTests.FinalizeAsync_Immediate_ReconcilesTheOfficeProjectAfterCommit` | expected `[commit, reconcile]`, actual `[commit]` |
| No reconcile when the save conflicts | `Handle_SaveConflict_DoesNotReconcile`, `FinalizeAsync_SaveConflict_DoesNotReconcile` | guards (passed before the fix) |
| Best-effort contract | `OfficeProjectReconcilerIntegrationTests.TryReconcileAsync_WhenReconcileThrows_ReturnsNullInsteadOfThrowing` | compile failure (new member) |
| Soft-deleted owner membership | `SoftDeletedOwnerMembership_IsRestored_NotDuplicated` | row still `is_deleted = true` |
| Lost create race | `LostCreateRace_ToHrLedEnsure_StillEndsOwnerLed` | expected `LeadSwitched`, actual `Created` |
| Next-owner fallback | `EarliestOwnerWithNoResolvableCompany_FallsThroughToNextOwner` | expected `Created`, actual `NoOwnerEmployee` |
| Partial earlier run converges | `PartiallyAppliedEarlierRun_NextRunConverges` | guard (passed before the fix) |
| Startup retry: tenant | `ExecuteAsync_TenantFailsOnce_IsRetriedInALaterAttempt` | reconciled once, expected twice |
| Startup retry: tenant list | `ExecuteAsync_TenantListFailsOnce_RunIsRetried` | tenant never reconciled |
| Startup retry bound | `ExecuteAsync_TenantAlwaysFails_StopsAfterMaxAttempts` | reconciled once, expected 3 |
| Deterministic paging (real Postgres) | `OfficeProjectReconciliationStartupPagingIntegrationTests.RunOnceAsync_TenantCreatedMidRun_EveryExistingTenantReconciledExactlyOnce` | a tenant reconciled 2 times |

## 3. Verification at `5a7e18d8` (2026-10-14)

| Suite | Result |
|---|---|
| `dotnet build src/ONEVO.Api` | 0 errors |
| Unit (`tests/ONEVO.Tests.Unit`) | 5623 / 5623 |
| Architecture | 717 / 717 |
| Integration, affected areas (`OfficeProject`, `MeetingCategoryBackfill`, `Onboarding`, `TaskCategor`, `Checklist`, `SensitivePositionChange`, `CrossLegalEntityInvitation`, `Tenan`) | 174 / 174 |
| Integration, full suite | 582 / 584: the 2 pre-existing failures below |
| Frontend `ng test` (quiet machine) | 4058 / 4059: the 1 pre-existing failure below |

## 4. Pre-existing failures (evidence)

Base commit `3df349ee` differs from `origin/development` (merge-base `6a20c095`) in **one docs file only** (the spec), so it runs `development`'s code exactly.

### Backend: 2 My Team integration tests

Command (run on a detached worktree at `3df349ee`, then on the branch HEAD):

```
dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj --filter "FullyQualifiedName~LedWorkProgressIntegrationTests|FullyQualifiedName~MyTeamCapabilitiesIntegrationTests"
```

| Test | Base `3df349ee` | Branch | Message (identical on both) |
|---|---|---|---|
| `MyTeam.MyTeamCapabilitiesIntegrationTests.CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row` | FAIL | FAIL | `Expected withBoth.Value!.CanViewPeopleStatus to be True, but found False.` |
| `MyTeam.LedWorkProgressIntegrationTests.Lead_sees_rolled_up_counts_member_sees_nothing` | FAIL | FAIL | `Expected member.Value!.Projects to be empty because plain membership never makes someone a lead, but found at least one item` |

Base result: `Failed: 2, Passed: 3, Total: 5`. Both are deterministic (they also fail when run alone) and are in My Team code that this branch doesn't touch.

### Frontend: 1 date-dependent spec

Command: `npx ng test --watch=false` (full) and `--include` of the spec alone, on base `5464686f` and on the branch.

| Spec | Base `5464686f` | Branch | Message |
|---|---|---|---|
| `work/ui/task-form-modal/task-form-modal.component.spec.ts` › "renders Due Date chip with consistent tfm-chip styling and label" | FAIL (full: `1 failed / 4054 passed`) | FAIL | `Expected: "Oct 15"  Received: "Tomorrow"` (depends on today's date) |

**Timing flakes, not failures:** `sub-sidebar.component.spec.ts` and `shared/utils/flip-animate.spec.ts` failed only in full runs made while the backend integration suite was loading the CPU. Each passes on its own, and the full suite on a quiet machine shows only the task-form-modal failure, on both base and branch. The branch's frontend diff is 6 files in the task-category model, mapper and editor.

## 5. Still deferred (minor, not changed)

- The primary-company preference ignores `LegalEntity.IsActive`, while the fallback requires it.
- The Edit API rejects an unchanged name on leftover "Meeting" case-variant categories. The reorder endpoint is unaffected.
- `StartAsync_InTestEnvironment_DoesNothing` would also pass without the guard.
- The backfill's `SET LOCAL app.tenant_context_mode = 'admin'` is not exercised under the NOBYPASSRLS migrator role.
- The lock badge's `aria-label` sits on a plain `<span>`; it needs `role="img"`.
