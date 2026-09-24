# File Creation Rules — `docs/superpowers/plans/` and `docs/superpowers/specs/`

Governs how files are created, sized, and organized inside `docs/superpowers/plans/` and `docs/superpowers/specs/`. Established 2026-08-06, per user request, alongside the restructure of `plans/` into `finished/` + `next/`; extended the same day to `specs/`. Referenced from `PROCESS_RULES.md` rule 7.

**Scope:** `docs/superpowers/plans/` and `docs/superpowers/specs/` only, for now. Other `docs/superpowers/` folders (`project_ core/`, `rules/`, `workflow/`) keep their existing structure and are not affected by this rule.

## 0. Also applies to `specs/`

`specs/` follows the same two-folder split as `plans/`:
- `specs/finished/` — the design's corresponding plan in `plans/finished/` has been executed and reviewed clean.
- `specs/next/` — the design is approved but its plan is still `pending` in `plans/next/`, or no plan has been written yet.

A design's own `**Status:** Approved...` line is the *brainstorm-approval* status, not the finished/next classification — finished/next here tracks *implementation* status, cross-referenced from `plans/SUMMARY.md`. Rules 2-6 below were written for `plans/` but apply the same way to `specs/` (status tracked in `specs/SUMMARY.md`, size/splitting rule for new designs, migration-not-retroactive-split for existing ones, per-folder SUMMARY.md, deferred `pin/`).

## 1. Folder layout

```
docs/superpowers/plans/
├── SUMMARY.md          — master index: every plan, its status, its location
├── finished/            — completed plans and point-in-time reports,
│   │                       split into one date folder per completion date
│   ├── SUMMARY.md
│   ├── 2026-08-03/
│   │   └── ...files completed on 2026-08-03...
│   ├── 2026-08-04/
│   │   └── ...files completed on 2026-08-04...
│   └── ...
├── next/                 — not-yet-finished plans (status: pending) + raw,
│   │                       not-yet-brainstormed future-feature context.
│   │                       STAYS FLAT — no date subfolders here.
│   └── SUMMARY.md
└── pin/                  — reserved for files specifically flagged as important
                            (created only when something is actually pinned —
                            see rule 6)
```

`finished/` and `next/` are the only two status folders — nothing else lives loose or in a personal subfolder under `plans/`. (A `kajaa/` personal folder existed briefly during the 2026-08-06 restructure; it was dissolved the same day into `finished/` per user request, so the split-by-status rule applies uniformly regardless of who authored a plan.)

**The date-folder split applies only inside `finished/`, per user request** — `next/` is intentionally left flat since pending work doesn't have a completion date yet. When a file's implementation finishes and it moves from `next/` to `finished/`, it lands in `finished/<YYYY-MM-DD>/` for the date it was completed — create that date folder if it doesn't exist yet.

For files that predate this rule and have no date in their filename (e.g. `*_REPORT.md`), use a `**Date:**` header if the file has one; otherwise infer the date from content (companion Part-report dates, referenced migration filenames, or the plan it corresponds to) and note in the folder's `SUMMARY.md` that the date was inferred, not stated.

Do not add a new file directly loose in `plans/` or in `plans/finished/`. Every file belongs in `next/`, a dated subfolder of `next/` (per rule 3, for an in-progress multi-part plan), or once finished, the matching `finished/<date>/`.

## 2. Every plan has a status

A plan is either `pending` (not finished) or `finished` (implementation done, reviewed clean). Status lives in `plans/SUMMARY.md`'s status table — every plan must have a row there.

- New plan → create it in `next/` with status `pending`.
- Implementation finishes (all tasks done, final review clean) → move the file (or its whole `<date>-<topic>/` folder, if it was split per rule 3) from `next/` into `finished/<completion-date>/`, then update its status to `finished` in `plans/SUMMARY.md`, and update `finished/SUMMARY.md` + `next/SUMMARY.md` accordingly.

## 3. When a plan needs more than one file

Target a maximum of ~300 lines per file. But line count is not the actual trigger for splitting — **the real test is whether one file can fully explain and let someone complete one task.** If a plan covers a feature too large for that, split the *feature* into independent sub-tasks first, then give each sub-task its own file. Do not just cut a long file in half at the 300-line mark; that produces two files that both still depend on each other to make sense.

Structure for a split plan:

```
plans/next/<YYYY-MM-DD-topic>/
├── part-1.md   — one fully self-contained sub-task
├── part-2.md   — another fully self-contained sub-task
└── ...
```

Each part file must stand alone: if you hand only `part-2.md` to an agent or developer with no access to `part-1.md` or any sibling file, they must have everything they need to understand and complete that part's task. If a part needs context from another part to make sense, the split was done at the wrong boundary — split by independent task, not by line count.

When the whole plan finishes, move the entire `<YYYY-MM-DD-topic>/` folder into `finished/<completion-date>/`.

## 4. This rule applies going forward, not retroactively

Rule 3's part-1/part-2 splitting is a **strict rule for new plan creation only.** Existing files are migrated by moving them into `finished/` or `next/` and recording a status — never by retroactively splitting them into parts. (The 2026-08-06 migration of 42 pre-existing files into `finished/` did exactly this: moved and status-tagged, not split, regardless of file length. The follow-up same-day split of `finished/` into per-date subfolders was also a pure move — no file content was touched.)

## 5. SUMMARY.md requirements

- `plans/SUMMARY.md` is the master index: it must list every plan with its status and which subfolder it lives in.
- `plans/finished/SUMMARY.md` and `plans/next/SUMMARY.md` each list the files physically inside that folder, per the general per-folder-SUMMARY convention in `PROCESS_RULES.md` rule 3.

## 6. The `pin/` subfolder

Each main folder covered by this rule may have a `pin/` subfolder to hold files specifically flagged as important enough to surface immediately (e.g. the current active plan, or a reference doc read every session). There is no fixed criteria for what gets pinned — it's a manual, case-by-case call. `plans/pin/` does not exist yet as of 2026-08-06; create it (and list its contents in `plans/SUMMARY.md`) the first time a file actually needs pinning, not preemptively.
