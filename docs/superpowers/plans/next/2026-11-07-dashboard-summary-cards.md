# Dashboard Summary Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At 2 rows tall, the four My Team widgets show donut → colour key → up to 4 animated summary cards (each summarising a popup section) as they get wider, replacing the wide stacked bar.

**Architecture:** One shared Angular component `app-dashboard-summary-strip` owns the 2-row layout (measures its own width, decides key yes/no and card count, renders the donut, key and cards). One presentational `app-dashboard-summary-card` renders a typed card model. Each widget only builds `SummaryCardModel[]` from data it already loads (plus two new backend sorts for rankings) and swaps its `mini` view template for the strip.

**Tech Stack:** Angular 19 standalone + signals, Jasmine/Karma (`ng test`); .NET 8 / xUnit backend.

**Spec:** `Hrms--Web-application---front-end---v1/docs/superpowers/specs/next/2026-11-07-dashboard-summary-cards-design.md`

FE root below = `Hrms--Web-application---front-end---v1/src/app/modules/dashboard/`. BE root = `HRMS-Backend-v1/`.

## Global Constraints

- Layout constants: donut 140 (`DASHBOARD_MINI_DONUT_PX`), gap 14, KEY_MIN 220, CARD_MIN 240, card chrome width 32, max 4 cards, hysteresis 20px.
- Card colours: background `color-mix(tone 6%, #fff)`, border tone 22%, icon badge tone 18%. Delta up `rgb(5 150 105)`, down `rgb(220 38 38)`. Text slate scale from `team-attendance-card.component.css`.
- A card never scrolls or clips; `font-variant-numeric: tabular-nums` on numbers; percentages via existing `formatPercent`.
- Animation: stagger 90ms per card, rise 8px + fade 400ms, count-up 700ms ease-out cubic, bars 45ms stagger, sparkline draw 800ms; all disabled under `prefers-reduced-motion`.
- Each component uses `styleUrl` (never a shared imported styles const — build hang).
- Zero warnings: `npm run check:warnings` and `npm run lint` clean; no `?.`/`??` on non-nullable types.
- Accessibility: card is a `<button type="button">` (or `role="button" tabindex="0" appKeyboardClick`), aria-label "`<title>`: `<figure>`. Open details".
- Card numbers must come from the same endpoint as the popup section they summarise.
- Do not commit or push unless the user asks.

## Review Focus

- Team of 1, or a ranking with fewer than 3 people → rank card shows only the rows it has; zero rows shows "No one needs attention" (or the card's empty line), never an empty box. (Task 2 test)
- Null figures (rate/utilization/coverage null, planner off) → figure shows "—", no delta line, chart track empty; no NaN. (Task 2 test + each widget task)
- Widget resized back and forth around a threshold → no flicker (hysteresis) and no re-fetch storm: ranking requests keyed, fired only when the card becomes visible. (Task 1 test + Task 5 test)
- Ranking request fails → only that card shows "Couldn't load · Try again"; donut, key and other cards still render. (Task 5 test)
- Clicking a card with keyboard (Enter/Space) opens the popup scrolled to its section. (Task 3 test)

---

### Task 1: Strip layout util

**Files:**
- Create: `FE/utils/summary-strip-layout.util.ts`
- Test: `FE/utils/summary-strip-layout.util.spec.ts`

**Interfaces:**
- Produces: `SUMMARY_STRIP_PX = { donut: 140, gap: 14, keyMin: 220, cardMin: 240, chrome: 32, maxCards: 4 }`; `interface SummaryStripPlan { key: boolean; cards: number }`; `planSummaryStrip(hostWidth: number, available: number, previous?: SummaryStripPlan | null): SummaryStripPlan` (`available` = how many card models the widget has; result `cards ≤ available`).

- [ ] **Step 1: Write failing tests** — `planSummaryStrip(292, 4)` → `{key:false,cards:0}`; `(424, 4)` → `{key:true,cards:0}`; `(900, 4)` → `{key:true,cards:1}`; `(1812, 4)` → `{key:true,cards:4}`; `(1812, 2)` → `cards:2`; `(0, 4, {key:true,cards:2})` → previous; hysteresis: previous `{key:true,cards:1}` at a width 10px under the 1-card threshold stays `cards:1`, 30px under drops to 0.
- [ ] **Step 2: Run** `npx ng test --include=src/app/modules/dashboard/utils/summary-strip-layout.util.spec.ts --watch=false` → FAIL (module not found).
- [ ] **Step 3: Implement.** body = hostWidth − chrome; key when body ≥ donut+gap+keyMin; cards = floor((body − donut − gap − keyMin − gap + gap) / (cardMin + gap)) clamped to [0, min(maxCards, available)]; hysteresis like `resolveTeamAttendanceView` (test ±20 on width keeps previous if it would still produce previous).
- [ ] **Step 4: Run** same command → PASS.

### Task 2: Summary card component + icons

**Files:**
- Create: `FE/models/summary-card.model.ts`
- Create: `FE/ui/dashboard-summary-card/dashboard-summary-card.component.{ts,html,css,spec.ts}`
- Modify: `src/app/shared/ui/icon/icon.component.ts` (add icon names)
- Create: `FE/utils/summary-card-format.util.ts` + spec

**Interfaces:**
- Produces (model):
```ts
export type SummaryTone = string; // a hex colour
export interface SummaryDelta { text: string; good: boolean; suffix: string } // "+2.0 pp", true, "vs last week"
export interface SummaryRankRow { id: string; name: string; avatarFileId: string | null; chips: { text: string; tone: SummaryTone }[] }
export type SummaryCardBody =
  | { kind: 'spark'; figure: string; delta: SummaryDelta | null; points: (number | null)[] }
  | { kind: 'bars'; figure: string; delta: SummaryDelta | null; values: number[] }
  | { kind: 'pair'; figure: string; delta: SummaryDelta | null; pairs: [number, number][]; legend: [string, string] }
  | { kind: 'meter'; figure: string; delta: SummaryDelta | null; percent: number | null; caption: string }
  | { kind: 'hbars'; headline: string; rows: { label: string; percent: number | null; tone: SummaryTone }[] }
  | { kind: 'rank'; count: string | null; countLabel: string; rows: SummaryRankRow[]; empty: string }
  | { kind: 'people'; count: string; countLabel: string; rows: { id: string; name: string; avatarFileId: string | null; chip: string }[]; empty: string }
  | { kind: 'chips'; count: string; countLabel: string; items: { label: string; value: number; tone: SummaryTone }[] };
export interface SummaryCardModel {
  id: string;              // stable, e.g. 'att-rate'
  title: string;
  icon: IconName;
  tone: SummaryTone;
  section: string;         // popup section id to scroll to
  state: 'ready' | 'loading' | 'error';
  body: SummaryCardBody | null; // null unless ready
}
```
- Produces (component): selector `app-dashboard-summary-card`; inputs `card: SummaryCardModel` (required), `index: number` (stagger); outputs `opened: string` (emits `card.section`), `retry: void` → name it `retried` (no DOM event names).
- Produces (format util): `summaryDelta(value: number | null, unit: 'pp' | '%' | 'h' | '', higherIsBetter: boolean, suffix: string): SummaryDelta | null` ("+2.0 pp", "−3 h"; null for null/undefined).
- New `IconName`s (lucide path data, same 24px stroke style): `calendar-check`, `building`, `alert-circle`, `plane`, `folder`, `bar-chart`, `user-x`, `activity`, `pie-chart`, `arrows-diff`, `list-checks`, `calendar-clock`, `trending-up`, `trending-down`.

- [ ] **Step 1: Failing tests for `summaryDelta`** — `(2, 'pp', true, 'vs last week')` → `{text:'+2.0 pp', good:true}`; `(-3, 'h', false, '')` → `{text:'−3 h', good:true}`; `(0,'pp',true,'')` → text `'±0 pp'`, good true; `(null, …)` → null.
- [ ] **Step 2: Failing component tests** — renders title + icon; `spark` with points `[1,null,3]` draws a path with no `NaN` in `d`; `figure: '—'` with `delta: null` renders no delta line; `rank` with 0 rows shows `body.empty` text; `rank` with 2 rows renders 2 `[data-testid=summary-rank-row]`; `state:'loading'` shows `[data-testid=summary-skeleton]`; `state:'error'` shows "Couldn't load" and clicking Try again emits `retried` and not `opened`; clicking the card (and keydown Enter) emits `opened` with `card.section`; host has class `summary-card--on` after the first animation frame (use fakeAsync + `tick`); with `matchMedia('(prefers-reduced-motion: reduce)')` mocked true, count-up shows final figure immediately.
- [ ] **Step 3: Run** `npx ng test --include='src/app/modules/dashboard/ui/dashboard-summary-card/**/*.spec.ts' --include=src/app/modules/dashboard/utils/summary-card-format.util.spec.ts --watch=false` → FAIL.
- [ ] **Step 4: Implement.** Template per kind as in the approved mockup (header row; figure 1.5rem/700; delta 0.6875rem; viz pinned to bottom 38px). Count-up animates the first number found in `figure` via rAF in an `afterNextRender`/effect, restoring the exact string at the end. Bars/meter/hbars widths start 0 and are set after one frame (CSS transitions). Sparkline: SVG viewBox 0 0 100 38, `preserveAspectRatio="none"`, `pathLength=1` dash draw, null points skipped (path breaks). Rank avatars use `app-avatar size="xs"`/`sm` with `buildFileUrl`. Card element is a `<button>` with the aria-label from Global Constraints; the Try again button stops propagation.
- [ ] **Step 5: Run** → PASS.

### Task 3: Summary strip component

**Files:**
- Create: `FE/ui/dashboard-summary-strip/dashboard-summary-strip.component.{ts,html,css,spec.ts}`

**Interfaces:**
- Consumes: Task 1 `planSummaryStrip`, Task 2 component/model, existing `DashboardDonutComponent`, `DonutKeyComponent`, `injectHostSize()` (`utils/host-size.util.ts`).
- Produces: selector `app-dashboard-summary-strip`; inputs `slices: DashboardDonutSlice[]`, `keySlices: DashboardDonutSlice[]`, `cards: SummaryCardModel[]`, `donutLabel: string`, model `active` (two-way, `string | null`, passed to donut + key); content projection = donut centre; outputs `cardOpened: string` (section), `cardRetried: string` (card id), `shownCards: number` (emitted when the plan's card count changes, so widgets can lazy-load).

- [ ] **Step 1: Failing tests** (use `host-size.testing.ts` to fake the width): at 292 → donut only, no `app-donut-key`, no cards; at 424 → key, no cards; at 1812 with 4 cards → 4 `app-dashboard-summary-card`, `shownCards` emitted 4; shrinking to 900 emits 1; a card `opened` re-emits as `cardOpened`; projected centre text renders inside the donut.
- [ ] **Step 2: Run** `npx ng test --include='src/app/modules/dashboard/ui/dashboard-summary-strip/**/*.spec.ts' --watch=false` → FAIL.
- [ ] **Step 3: Implement.** Flex row, height 100%, gap 14; donut fixed 140; key `variant="list"` `[showValue]="true"` width 220–256 (flex none); cards `flex: 1 1 0; min-width: 0`.
- [ ] **Step 4: Run** → PASS.

### Task 4: Backend — attendance `attention` sort and led-employee `completionRate` sort

**Files:**
- Modify: `BE/src/ONEVO.Application/Features/TimeAttendance/Team/Queries/TeamAttendanceQueries.cs:11` (doc comment)
- Modify: `BE/src/ONEVO.Application/Features/TimeAttendance/Team/Queries/TeamAttendanceQueryHandler.cs:141-146`
- Modify: `BE/src/ONEVO.Application/Features/WorkManagement/Leadership/RepositoryInterfaces/ILedProgressReadRepository.cs:13` (enum `CompletionRate`)
- Modify: `BE/src/ONEVO.Application/Features/WorkManagement/Leadership/Services/LedProgressQueryOptions.cs` (`["completionRate"]`)
- Modify: `BE/src/ONEVO.Infrastructure/Persistence/Repositories/WorkManagement/EfLedProgressReadRepository.cs:153-158`
- Test: `BE/tests/ONEVO.Tests.Unit/Features/TimeAttendance/TeamAttendanceQueryHandlerTests.cs`, `BE/tests/ONEVO.Tests.Unit/Features/WorkManagement/Leadership/LedProgressDetailQueryHandlerTests.cs` (validator accepts `completionRate`); integration test for the EF ordering beside the existing led employee-hours repository tests if one exists, else a unit test of the ordering helper.

**Interfaces:**
- Produces: `GET …/team/attendance-employees?sort=attention` → order by (late + absent + onLeave days in period) desc, then name; people with 0 issues after everyone with issues. `GET work/led-progress/employees?sort=completionRate&direction=asc` → completed ÷ (completed + inProgress + notStarted) asc; people with 0 total hours last regardless of direction.

- [ ] **Step 1: Failing tests** — attendance: 3 employees (A: 2 late, B: 1 absent + 2 leave, C: none) with `Sort="attention"` → order B, A, C; validator/handler still treats unknown sort as name. Led: validator accepts `completionRate`; ordering with A 10/20 h, B 1/10 h, C 0/0 h asc → B, A, C.
- [ ] **Step 2: Run** `dotnet test tests/ONEVO.Tests.Unit --filter "FullyQualifiedName~TeamAttendanceQueryHandlerTests|FullyQualifiedName~LedProgressDetail"` → FAIL.
- [ ] **Step 3: Implement** the two orderings (attendance in the in-memory LINQ beside `"rate"`; led as a ternary on the EF row projection: `total == 0` key first, then `Completed / total`).
- [ ] **Step 4: Run** the same filter → PASS; then `dotnet build src/ONEVO.Api -c Release` clean.
- [ ] **Step 5: FE models** — add `'attention'` to `TeamAttendanceEmployeesQuery.sort` and `'completionRate'` to `LedEmployeeSort`.

### Task 5: Team attendance cards

**Files:**
- Create: `FE/utils/team-attendance-summary-cards.util.ts` + spec
- Modify: `FE/feature/team-attendance-card/team-attendance-card.component.{ts,html,css,spec.ts}`
- Modify: `FE/feature/team-attendance-detail-dialog/team-attendance-detail-dialog.component.{ts,html}` (input `focusSection`)
- Modify: `FE/utils/team-attendance-layout.util.ts` (drop `miniBarWidth`, `miniFigureWidth` and their signals)

**Interfaces:**
- Consumes: Tasks 2–4.
- Produces: `teamAttendanceSummaryCards(data: TeamAttendanceSummaryResponse, attention: { rows: TeamAttendanceEmployeeRow[] | null; error: boolean }): SummaryCardModel[]` — order: `att-rate` (spark, section `trend`), `att-departments` (hbars, section `departments`), `att-attention` (rank, section `ranking`), `att-leave` (people, section `today`).
- Produces (dialog): `focusSection = input<string | null>(null)`; on open, `scrollIntoView({block:'start'})` the element with `[data-section="<id>"]`. Every popup in Tasks 5–8 gets the same input and `data-section` attributes.

- [ ] **Step 1: Failing util tests** — rate figure `'96%'`, delta from `deltaPp` (pp, higher better), points from `trend[].rate`; `periodRate: null` → figure `'—'`, delta null; departments headline `Overall 94%`, top 3 rows, a row under 90% gets amber `#f59e0b`; attention chips from each row's `days` buckets ("3 late", "1 absent", "2 leave", omitting zeros), state `loading` when rows null and not error, `error` when error; leave card count = `todayCounts.onLeave`, rows = `todayPreview` with bucket `leave` (max 3), chip = department or "On leave".
- [ ] **Step 2: Failing card test** — at mini width 1812 the card renders `app-dashboard-summary-strip` with 4 cards and no `[data-testid=ta-status-bar]`; the employees API is called once with `sort: 'attention', pageSize: 3` only after `shownCards ≥ 3`; a failed call shows the error card while the other three render; clicking a card opens the dialog with `focusSection` set.
- [ ] **Step 3: Run** `npx ng test --include='src/app/modules/dashboard/**/team-attendance*.spec.ts' --watch=false` → FAIL.
- [ ] **Step 4: Implement** — replace the whole `@case ('mini')` block with the strip (centre = today's rate + "today"); keep xs/standard untouched.
- [ ] **Step 5: Run** → PASS.

### Task 6: Team progress cards

**Files:**
- Create: `FE/utils/team-progress-summary-cards.util.ts` + spec
- Modify: `FE/feature/team-progress-card/team-progress-card.component.{ts,html,css,spec.ts}`, `FE/feature/team-progress-detail-dialog/…` (`focusSection`)

**Interfaces:**
- Produces: `teamProgressSummaryCards(led: LedWorkProgressResponse, weekly: LedWeeklyCompletion | null, behind: { rows: LedEmployeeProgress[] | null; error: boolean }): SummaryCardModel[]` — `tp-weekly` (bars: last bucket completedHours as `"312 h"`, delta % vs previous bucket, last 7 buckets; section `weekly`), `tp-projects` (hbars: `"<n> active projects"`, top 3 by completed ÷ allocated %; section `projects`), `tp-overdue` (rank: count `overdueTotal`, label "overdue", top 3 `overdueTasks` by `daysOverdue` desc, chip `"<d> d late"` red ≥ 3 else amber, avatar from first assignee initials; section `overdue`), `tp-behind` (rank: chips `"<pct>% done"`; section `members`).

- [ ] **Step 1: Failing util tests** — exact figures/labels above; `weekly: null` → `tp-weekly` state loading; project with 0 allocated → percent null shown "—"; overdue empty → empty text "Nothing overdue".
- [ ] **Step 2: Failing card test** — mini wide renders strip with 4 cards; employees API called with `sort:'completionRate', direction:'asc', pageSize:3` only after `shownCards ≥ 4`; weekly completion fetched once (reuse the existing `getWeeklyCompletion(10)` subscription the card already makes).
- [ ] **Step 3: Run** `npx ng test --include='src/app/modules/dashboard/**/team-progress*.spec.ts' --watch=false` → FAIL.
- [ ] **Step 4: Implement** the mini swap (centre = completed % + "of N h").
- [ ] **Step 5: Run** → PASS.

### Task 7: Today's team schedule cards

**Files:**
- Create: `FE/utils/team-schedule-summary-cards.util.ts` + spec
- Modify: `FE/feature/team-schedule-card/team-schedule-card.component.{ts,html,css,spec.ts}`, `FE/feature/team-schedule-detail-dialog/…` (`focusSection`)

**Interfaces:**
- Produces: `teamScheduleSummaryCards(day: TeamScheduleResponse, week: TeamScheduleWeekResponse | null): SummaryCardModel[]` — `ts-coverage` (meter: `week.totals.coveragePercent`, caption `"<scheduled> / <planned> h planned"`, delta pp vs `week.previous.coveragePercent`; section `coverage`), `ts-plan-actual` (pair: `week.days` planned (task+meeting) vs clocked hours, figure = clocked ÷ planned %, legend ["Planned","Actual"]; section `planned-actual`), `ts-needs-plan` (rank: members with `notRankedReason === 'no_planned_task_time'` first (chip "not planned", slate), then members with `expectedMinutes` and planned > expected sorted by load desc (chip `"<load>% load"`, red ≥ 120 else amber), max 3, empty "Everyone has a plan"; section `ranking`), `ts-now` (chips by `presenceStatus` mapped to Working / In meeting / On break / On leave, plus Not planned = count of `no_planned_task_time`; count = `summary.people`; section `status`).
- Week null → `ts-coverage` and `ts-plan-actual` state loading. Planner off (`plannerEnabled false`) → coverage figure "—", caption "Planner is off".

- [ ] **Step 1: Failing util tests** for each rule above, incl. planner off and a member with `expectedMinutes: null` never counted as over capacity.
- [ ] **Step 2: Failing card test** — mini wide renders strip with 4 cards; week store loads (existing `weekStore.load(null)`) when `shownCards ≥ 1`.
- [ ] **Step 3: Run** `npx ng test --include='src/app/modules/dashboard/**/team-schedule*.spec.ts' --watch=false` → FAIL.
- [ ] **Step 4: Implement** the mini swap (donut slices = the status counts used by `ts-now`; centre = coverage %).
- [ ] **Step 5: Run** → PASS.

### Task 8: Work vs task hours cards

**Files:**
- Create: `FE/utils/team-work-task-summary-cards.util.ts` + spec
- Modify: `FE/feature/team-work-task-card/team-work-task-card.component.{ts,html,css,spec.ts}`, `FE/feature/team-work-task-detail-dialog/…` (`focusSection`), `FE/utils/team-work-task-layout.util.ts` (drop `miniBarWidth`)

**Interfaces:**
- Produces: `teamWorkTaskSummaryCards(data: TeamWorkTaskHoursResponse): SummaryCardModel[]` — `wt-share` (spark: `totals.utilization` %, delta pp vs `previous.utilization`, points `trend[].utilization`; section `share`), `wt-days` (pair: trend worked vs task hours, figure `"<worked> / <task> h"`, delta = worked − task hours "not on tasks" (good = false when > 0); legend ["Worked","Task"]; section `daily`), `wt-gap` (rank: employees by `workedMinutes − taskMinutes` desc, only gap > 0, chip `"+<h> h"` red ≥ 8 h else amber, empty "No gaps"; section `employees`), `wt-done` (bars: `completedTasks`, delta vs `previous.completedTasks`, values `trend[].completedTasks`; section `tasks`).

- [ ] **Step 1: Failing util tests** — figures above; utilization null → "—"; employee with 0 worked minutes never ranked.
- [ ] **Step 2: Failing card test** — mini wide renders strip with 4 cards; no extra HTTP call.
- [ ] **Step 3: Run** `npx ng test --include='src/app/modules/dashboard/**/team-work-task*.spec.ts' --watch=false` → FAIL.
- [ ] **Step 4: Implement** the mini swap.
- [ ] **Step 5: Run** → PASS.

### Task 9: Gate and browser pass

- [ ] **Step 1:** `pwsh ../dev-env/scripts/verify.ps1 frontend` → build + unit tests green; `npm run check:warnings` → no `[WARNING]`/`[ERROR]`; `npm run lint` → 0/0.
- [ ] **Step 2:** `dotnet test tests/ONEVO.Tests.Unit` and `tests/ONEVO.Tests.Architecture` green.
- [ ] **Step 3:** Browser pass at https://onexso.com:4200/ (user signs in): each widget forced to 2×2, 2×4, 2×6, 2×8, 2×12 via `gridArea`; for each, measure the frame's overflow div `scrollHeight − clientHeight === 0`; screenshot 2×12 for all four; click one card per widget → popup opens at its section.
- [ ] **Step 4:** Update memory + `docs/reports/` summary; move spec + plan from `next/` to `finished/` after the user's own browser check.
