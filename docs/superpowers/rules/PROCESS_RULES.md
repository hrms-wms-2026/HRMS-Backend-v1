# Documentation & Process Rules

These rules govern how `docs/superpowers/` is maintained. They apply to every future task in this project unless the user explicitly overrides one.

## 1. Plan before non-trivial work

Write an implementation plan to `docs/superpowers/plans/YYYY-MM-DD-<topic>.md` before starting code or document changes, for anything beyond a one-line fix.

## 2. Keep `project_ core/` documents current

Whenever a decision, code change, or investigation changes what the Architecture document(s) or `phase1-table-inventory.md` describe, update that document as part of finishing the task — not as a deferred follow-up.

## 3. Every `docs/superpowers/` subfolder maintains its own `SUMMARY.md`

Update the folder's `SUMMARY.md` in the same change that adds, removes, or significantly edits a file in that folder, so a fresh session can load folder context without reading every file in it.

## 4. Verified reports outrank generic architecture claims

When a `workflow/` report explicitly states it is code-verified (for example `authentication.md`, which opens with "Source of truth: actual code") and it conflicts with more generic `project_ core/` architecture text, treat the workflow report as correct until the architecture doc is updated to match it.

## 5. Edit boundary

Only `project_ core/` documents (the architecture docs, the tables file) get content edits during doc-sync work. Other folders (`plans/`, `workflow/`) only receive **new** files (new plans, new workflow reports) — an existing file there is only changed if the user explicitly asks for that specific file to change.

**Origin:** established 2026-08-03, see `docs/superpowers/specs/2026-08-03-doc-audit-and-process-setup-design.md`.
