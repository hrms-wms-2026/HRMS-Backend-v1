# Part 6: Postman documentation gaps for the Sprint CRUD endpoints

**Status:** done (2026-08-21, completed directly — documentation-only, no code risk, didn't need Cursor)

## What this was

While auditing the expanded Tree/Sprint/Task requirement against `docs/postman-request/Work Management/`
(46 files at the time), 4 live, working endpoints were found to have zero documentation despite being fully
implemented and reachable: Create Sprint, Edit Sprint, Achieve Sprint, Complete Sprint. Everything else in
scope for this feature was either already documented or is covered by Parts 3-5's own doc tasks
(`Get Objective Tree`, `Delete Task`, and the `Create Task`/`Create Task Creation Request` updates).

## Files created

- `docs/postman-request/Work Management/Create Sprint.md`
- `docs/postman-request/Work Management/Edit Sprint.md`
- `docs/postman-request/Work Management/Achieve Sprint.md`
- `docs/postman-request/Work Management/Complete Sprint.md`

Each follows the standard 6-section format, written directly from the live
`SprintsController.cs`/`*SprintCommandHandler.cs` code (request/response shapes, exact authorization rule —
owning-Objective-owner-only, no bypass path for any of the four — and error tables), not from the plan
descriptions elsewhere in this folder.

## Note

No test/build impact — pure documentation. Not part of the Cursor execution prompt for Parts 3-5.
