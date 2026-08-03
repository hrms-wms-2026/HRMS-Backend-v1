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

## 6. Every finished API endpoint gets a Postman request

When a backend task finishes an API endpoint (controller action reachable over HTTP), add or update a matching request file before the task is considered done — do not defer this to a later cleanup pass.

- Location: `postman/collections/<Collection Name>/<NN. Section>/<Request Name>.request.yaml`, one file per request, following the existing `$kind: http-request` schema (see any existing file for the exact shape).
- Collection choice: tenant/customer-facing endpoints (`/api/v1/...`) go under `ONEVO Organization Admin API`; platform/admin endpoints (`/admin/v1/...`) go under `ONEVO Developer Platform API`.
- Section choice: reuse an existing numbered folder (e.g. `05. Password`) if the endpoint belongs to that area; otherwise create the next-numbered folder (e.g. `07. Work Management`) — `99. Health` always stays last.
- The request body must reflect the actual request contract (real field names, matching the controller's bound request type) — not a placeholder guess.
- `postman/` is git-ignored in this repo (synced separately through Postman's own workspace sync, not git) — writing the `.request.yaml` file to disk is the complete action; it is never staged or committed.

**Origin:** established 2026-08-03, per user request after the Work Management Foundation slice shipped without one.
