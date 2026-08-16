# Sub-agent prompt: execute the allocation-overcommit fix plan (copy-paste this whole block)

Repo: `HRMS-Backend-v1`, branch `feature/work-management-milestone-membership` (current branch — stay
on it). Scope guardrail: **Work Management module only** — don't touch `organization`,
`layouts/main-layout`, or any other module; a teammate owns those.

## What to do

Execute the plan at
`docs/superpowers/plans/next/2026-08-17-work-management-allocation-overcommit-fix.md` task-by-task,
using **superpowers:executing-plans**. The plan is already fully detailed — it was written after live
root-cause investigation (browser repro against the running dev servers, full code tracing of both
bugs), not speculation, so you should not need to re-derive the diagnosis. Read the plan's
"Background" section first for the confirmed root causes before starting Task 1.

**Two bugs, two independent fixes:**
1. The dapi demo seeder's `ComputeChildHours` lets sibling Objectives collectively overcommit their
   parent (confirmed: HWPORTAL's root ends up at -3930h of "available slack"), which blocks task
   creation and allocation-extension approval anywhere it's hit. Fix: normalize the hours-distribution
   formula (Task 1 in the plan) — full replacement code is already written out in the plan, not left
   to your judgment.
2. Three handlers serialize the "insufficient allocation" 409 error body with default PascalCase
   casing, but the frontend expects camelCase, so the intended friendly error UI never triggers. Fix:
   a shared camelCase serialization helper, swapped in at all three call sites (Task 2 in the plan).

**Explicitly out of scope — do not touch:** `ObjectiveParentConstraintChecker`. The plan's Global
Constraints section explains why: its current per-child-vs-parent-only check is deliberate,
pre-existing, documented design, not a bug. Only the seed data's hours-distribution formula is broken.

**Task 3 (DB reset) requires the user's explicit go-ahead before you run it** — it's a destructive
step (drop/recreate the local dev database) and will also wipe any manually-created test data outside
the deterministic dapi-demo dataset (the plan calls out a specific example: a hand-created "hi" test
Objective). Stop and ask before doing this step; don't just run it because it's next on the checklist.

**Follow TDD per the plan:** each task's tests are written out in full already — write the failing
test first, confirm it fails for the right reason, then implement, then confirm it passes, then run
the broader regression suite specified in Task 5 before considering this done.

Once all 5 tasks are complete and verified, report back what changed and the final test run output.
