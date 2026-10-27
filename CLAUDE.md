# CLAUDE.md

Guidance for Claude Code (and humans) working in this .NET backend. Read `README.md` for layout and local run steps.

## Build and test

Use the dev-env gate scripts (outside this repo, in `../dev-env/scripts/`). They print one line per step and keep full logs in `../dev-env/reports/`:

- `pwsh ../dev-env/scripts/verify.ps1 backend` builds the API (Release) and runs unit + architecture tests. This matches the blocking CI job.
- `pwsh ../dev-env/scripts/verify.ps1 backend -Filter "FullyQualifiedName~WorkManagement"` runs targeted unit tests only. Use it while iterating, then run the full gate once at the end.
- `pwsh ../dev-env/scripts/verify.ps1 backend -Full` adds the Testcontainers integration suite. It needs Docker running.
- `pwsh ../dev-env/scripts/security-scan.ps1 backend -Baseline origin/development` runs the security scanners and shows only Semgrep findings new on this branch.

Raw commands, if you need them: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release` and `dotnet test tests/ONEVO.Tests.<Unit|Architecture|Integration>/...csproj -c Release`. There is no `.sln`.

## Rules

- **Tenant isolation is the top security property.** Every tenant-owned table needs RLS policy coverage, which the architecture tests enforce. A new tenant-owned table without a coverage migration fails `ONEVO.Tests.Architecture`. Never weaken or skip that test.
- Re-check `RequirePermission` on every new endpoint and every code path that calls a command directly (for example, Inbox providers call commands without going through the controller's permission attribute).
- There is no fallback authorization policy: an action without `[Authorize]` is anonymous. Public endpoints must say `[AllowAnonymous]` and authenticate another way. `pwsh ../dev-env/scripts/find-unauthorized-endpoints.ps1 backend` must report 0 unprotected actions.
- User-authored rich text (messages, task descriptions, comments) must be checked with `RichTextRules.HasActiveContent` on write.
- Do not apply migrations to a shared database yourself. Create them, report them, and let the developer apply them.
- Do not suppress analyzers or tests, and do not use `--no-verify`, to make a check pass.
- New prompts, summaries and reports go under `docs/` (`docs/superpowers/execution-prompts/`, `docs/superpowers/reports/`, `docs/design-notes/`), never at the repo root.

## Known gotchas

- `dotnet build` fails with a NuGet.targets "path1 null" error: this is a stale MSBuild server, not a code problem. Run `dotnet build-server shutdown` and retry.
- `-p:BuildProjectReferences=false` also skips rebuilding `ONEVO.Application`, so new Application code is not seen. Build `src/ONEVO.Application` first, then test with the flag.
- A migration generated from a stale checkout can silently corrupt `ApplicationDbContextModelSnapshot.cs`, which makes unrelated modules look broken. Diagnose the snapshot before assuming real model drift.
- An empty-body 404 from an endpoint usually means routing, not the handler. `{action}` is a reserved route parameter name in ASP.NET Core.
