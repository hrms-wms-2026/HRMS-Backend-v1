# Team Attendance Widget — Final Summary

Date: 2026-10-27 · Branch `feature/team-attendance-widget` in both repos (local, **not pushed**, no PR, no merge, no migration).
Spec: `docs/superpowers/specs/finished/2026-10-27/2026-10-27-team-attendance-widget-design.md` · Plan: `docs/superpowers/plans/finished/2026-10-27/2026-10-27-team-attendance-widget.md`

## What it is
My Team widget `team-attendance`: one card, three size-driven views (Small / Medium / Large) plus an Expanded popup. Week ↔ Month switch with ‹ › navigation in every view. Late counts as attended; leave and not-yet-in are outside the rate. Today comes from the Team Status engine; past days come from `AttendancePeriodCalculator.Classify`.

## Backend (base b8f47ac3)
- f8f6cfaf `TeamAttendanceMath`: ISO week / month ranges, clipped previous period, buckets, weighted rate.
- d399df90 Batched `ExcusedDatesForEmployeesAsync` and `ResolveThresholdsAsync`. `ResolveAsync` shares their precedence helper.
- ed6ef5d5 `TeamAttendancePeriodReader`: one batched load for the whole team, then Classify per employee.
- 63a92827 `TeamAttendanceQueryHandler`, plus `GET attendance/time-tracking/team/attendance-summary` and `/attendance-employees` (`attendance:read`).
- d1657d28 Registry entry (min 3x3, max 12x8, default rect (0,6,8,6) on My Team), access (same gate as team-status) and data provider.
- a92bf242 Review fix: off-day clock-ins aren't counted, and today's expected not-in-yet shows as Pending.
- **No migration or new table.** The work is bounded to 31 days and loaded in batches; existing per-employee/date indexes serve it.
- Tests: unit 6772/6772, architecture 736/736 (Release). Integration tests not run. Latency not measured.

## Frontend (base 1622fd53)
- 85e152ee Model, `TeamAttendanceApiService`, `TeamAttendanceStore` (shared period state, latest-wins, `seed` never resets the viewer's choice).
- c71c2d68 `team-attendance-layout.util` (small/medium/large + hysteresis) and `ui/attendance-day-chart` (stacked bars).
- 44618a47 `team-attendance-card` (3 views) + registry.
- 0537d282 `team-attendance-detail-dialog` (5 period tiles, trend, distribution, per-date matrix with sticky name column, search, department filter, paging, CSV export). CSV helpers moved to `shared/utils/csv.util.ts`; `employee-csv.ts` re-exports them.
- d2ad9a09 Review fixes: export failure message, and closing the popup clears its department filter.
- Reused: `app-widget-card`, `app-popout-header-actions`, `app-kpi-tiles`, `app-avatar`, `app-modal`, `app-select`, `app-input`, `injectWidgetDataFeed`, the CSV helpers, and the team-progress donut geometry and resize pattern.
- Tests: full `ng test` 4651/4652. The 1 failure is the pre-existing notification-settings spec. `npm run check:warnings` is clean.

## Pending for the user
1. Restart your API on the new backend code.
2. Browser pass on dapi: add the widget on My Team, check Small/Medium/Large/popup × Week/Month, at desktop and 375px.
3. Push / PR when happy.

## Rulings made during the build
- - T4->T5: TimeTrackingController.TeamAttendanceSummary action name must be used by provider gate — T4 must name the action TeamAttendanceSummary. Ruling: name actions TeamAttendanceSummary / TeamAttendanceEmployees — provider gate needs it — cost: rename.
- Task 2: Ruling: excused-batch test was written before the impl but its RED run was masked by the same-assembly compile failure of the resolver test — accepted; the test asserts real filter behaviour (outcome, tenant, range) — cost if wrong: a weaker proof the test can fail
- Task 3: Ruling: excused late -> Present, excused absent -> Off (not counted) — spec §4 says follow Classify's day list BUT also "must match the Overview if it drops excused days"; the Overview counts come from the post-excuse Late/Absent lists, so the match clause governs — cost if wrong: an excused day shows as Present/"–" instead of Late/Absent in the matrix.
- Task 3: Ruling: each employee's "today" = UtcNow converted to their own legal-entity timezone (not clock.Today) — spec §4 my-call on per-employee timezone — cost if wrong: a one-day edge difference vs Overview near midnight.
- Task 3: Ruling: no leave scope (provider missing or Scope null) => leave masked as Absent — mirrors team-today, where leaveScope null => not authorized — cost if wrong: authorized viewers see Absent if provider fails.
- Task 4: Ruling: department names come from ListEmployeeIdentitiesAsync (it already carries Department) instead of IDepartmentRepository — one fewer query, same names the history rows show — cost if wrong: an employee outside the actor's legal entity has no identity and so no department option.
- Task 4: Ruling: today's bucket for employees past the 200 team-today cap falls back to the reader's (Classify) value; unfiltered today counts still come from team-today's uncapped Summary — plan Review Focus #2 — cost if wrong: today matrix cells for >200 teams may differ from Team Status.
- Task 6: Ruling: seed() applies the bootstrap only when it is the selected period (week, no department, same rangeStart); otherwise it reloads the selected period — a dashboard refresh must not reset the viewer's Week/Month choice (spec §7) — cost if wrong: one extra request on refresh.
- Task 6: Ruling: setMode anchors on data.today when the shown period contains today, else on the current anchor — so Month->Week from the current month lands on this week, not the week of the 1st — cost if wrong: surprising period after switching.
- Task 6: Ruling: FE tasks are ledgered by hand (task-done script targets the backend repo); FE BASE = 1622fd53 (feature/customizable-dashboard).
- Task 7: Ruling: chart colours are fixed palette hex (green/amber/red/blue) like the team-progress card, not --color-* tokens — the dashboard cards already use fixed palette colours — cost if wrong: colours don't follow a tenant theme preset.
- Task 7: Ruling: narrow-tall medium needs width >= 420px; below that a tall card is small — the donut + legend + chart can't stack legibly narrower — cost if wrong: a 3-4 column tall card shows the small view.
- Task 8: Ruling: Small view ring is the status donut (dasharray, team-progress geometry via new utils/status-donut.util) with "attended / eligible" in the centre, not app-progress-ring — progress-ring only renders a % label in the accent colour; the approved mockup shows counts in a status-coloured ring — cost if wrong: one fewer shared-component reuse.
- Task 8: Ruling: period switch is a native <select> styled like team-progress's header select, not app-select — matches the sibling card's header control and avoids overlay clipping in the widget frame — cost if wrong: visual mismatch with app-select elsewhere.
- Task 8: Ruling: team-progress donut code left untouched (new util not back-ported) — spec non-goal: no change to other widgets — cost if wrong: ~10 lines of similar geometry in two places.
- Task 8: Ruling: TDD slip — widget-registry.ts edited before its spec was updated; spec updated and run green right after — cost if wrong: none observed.
- Task 8: Ruling: store gained loadedAt (header "Refreshed" label) — needed by app-popout-header-actions — spec assertion added.
- Task 9: Ruling: shared labels/colours/range+delta formatters moved to utils/team-attendance-format.util.ts — the dialog and card would otherwise import each other — cost: none.
- Task 9: Ruling: matrix is a plain sticky-column scroll table, not shared data-table — data-table has no sticky identity column + 31 dynamic columns; spec allows a plain scroll table — cost if wrong: table styling differs slightly from other lists.
- Task 9: Ruling: modal keeps app-modal's own header (title + close) instead of a custom header — fewer overrides, Esc/close/focus stay app-modal's — cost if wrong: header looks a little plainer than the mockup.
- Task 9: Ruling: downloadFile is a public field (= downloadCsv) so the export test can replace the browser download — ESM namespace exports can't be spied — cost: none.
- Final: Ruling: re-graded "export failure silent" Minor→Important — a user clicking Export gets no feedback — fixed, 'a failed export says so' RED→GREEN, FE suite 4651/4652 (baseline fail) (FE d2ad9a09)
- Final: Ruling: re-graded "department filter carries over to the card unseen" Minor→Important — card shows filtered team figures with no indicator — fixed by clearing the filter on dialog close, test RED→GREEN, FE suite 4651/4652 (FE d2ad9a09) — cost if wrong: the filter doesn't persist between dialog openings.
- Final: Ruling: declined-to-judge items (native select, palette colours, custom donut, excused-day handling, per-employee timezone, cross-entity department names, saved layouts not auto-getting the widget, polling refreshes dialog page, integration tests/latency not run) — all stand as prior rulings or known limits.

## Deferred minors
- extreme anchor (0001-01-01 / 9999-12-31) throws in AddDays/AddMonths → 500 instead of 400; not reachable from the UI.
- employees search/paging runs in memory per request and export issues ceil(n/50) full loads; fine at team scale, could trip the global rate limiter on very large teams.
- a late arrival who already clocked out shows Present today (Team Status's mapper only flags late for working/on_break), then Late from tomorrow.
- TeamSize (rows with an employee record) can differ by a few from unfiltered eligibleToday (all covered ids).
- root store keeps last data across sign-out/sign-in in the same tab (same as sibling stores).
