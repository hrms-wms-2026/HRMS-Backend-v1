# 1. Repository Baseline Report

## Table of Contents

1. [Repositories in Scope](#1-repositories-in-scope)
   1.1 [Identity and HEAD](#11-identity-and-head)
   1.2 [Repositories Out of Scope](#12-repositories-out-of-scope)
2. [Audit Environment](#2-audit-environment)
3. [Existing Documentation Reused](#3-existing-documentation-reused)
4. [CI Configuration](#4-ci-configuration)
5. [Branch State at Audit Time](#5-branch-state-at-audit-time)

## 1. Repositories in Scope

### 1.1 Identity and HEAD

| Item | Backend | Frontend (tenant web app) |
|---|---|---|
| Repository | `hrms-wms-2026/HRMS-Backend-v1` | `hrms-wms-2026/Hrms--Web-application---front-end---v1` |
| Integration branch | `development` | `Development` (capital D) |
| HEAD audited | `8ce94e88e7bb74fe41be19ae2c1a0be161fda8ed` | `1c6211b08993133a5c3a3b694b48d2719f89fcb7` |
| HEAD commit | Merge PR #202 `chore/uat-hardening` (2026-10-29) | Merge PR #137 `chore/uat-hardening` (2026-10-29) |
| Stack | .NET 10, ASP.NET Core, EF Core, PostgreSQL, MediatR | Angular 21, NgRx Signals, Vitest, Playwright |
| Size | 4,043 `.cs` files in `src/`; 134 controllers; 268 migrations | 553 spec files; 5,073 unit tests |
| Audit branch | `audit/uat-readiness-2026-10` | `audit/uat-readiness-2026-10` |

### 1.2 Repositories Out of Scope

- `HRMS-Platform-Administration-Front-End-v1` (platform admin app): not attached to this session.
- Tray App source: not in either repository. Only the backend endpoints the tray calls were reviewed.
- `../dev-env/` gate scripts referenced by both `CLAUDE.md` files: not in the repositories, so not available here. Equivalent raw commands were run instead.

## 2. Audit Environment

| Item | Value |
|---|---|
| Host | Linux cloud container, 4 CPU, 15 GB RAM |
| .NET SDK | 10.0.113 (Ubuntu package) |
| Node | 22.22.0 |
| Database | PostgreSQL 16, local, fresh database `OnevoUat` |
| Docker | **Not available.** Testcontainers integration tests could not run. |
| Browser | Chromium (Playwright), headless, browser time zone UTC |
| Cost tracking | **Not available.** No token or USD usage figures can be reported. |

## 3. Existing Documentation Reused

| Document | Author, date | Used for |
|---|---|---|
| `docs/reports/security/2026-10-27-uat-hardening-report.md` (BE) | kajaatharan, 2026-10-27 | Secret scan, anonymous endpoints, raw SQL, `IgnoreQueryFilters`, XSS fix, open items |
| `docs/reports/code-quality/2026-10-27-uat-hardening-report.md` (FE) | kajaatharan, 2026-10-28 | Angular CVEs, DOMPurify, lint backlog, open items |
| `docs/reports/documentation/2026-10-28-docs-and-code-gap-audit.md` (BE) | kajaatharan, 2026-10-28 | Documented-but-unbuilt features |
| `docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md` (BE) | kajaatharan, 2026-10-28 | WM-01 to WM-05 and SEC-13 status |
| `docs/superpowers/project_ core/MODULE_MAP.md` (BE), `docs/core/MODULE_MAP.md` (FE) | team | Module map |

## 4. CI Configuration

| Repository | CI | What runs |
|---|---|---|
| Backend | `.github/workflows/ci.yml` on push and PR to `main`, `testing`, `development`, plus a nightly cron | Restore, Release build of `ONEVO.Api`, unit tests, architecture tests. Integration tests run filtered by changed files on PRs, and in full on push and nightly. Results are uploaded as artifacts. |
| Frontend | **None.** No `.github/workflows` folder or any other CI configuration exists. | Only the local `.githooks/pre-push` hook (`npm run check:warnings`). It is skipped when the hook is not installed. |

## 5. Branch State at Audit Time

| Branch | Ahead of integration branch | Notes |
|---|---|---|
| BE `feature/planner-redesign-backend` | 25 commits | Holds `GET /planner/me/range` and `/me/focus`, which the frontend `Development` already calls (see UAT-001). |
| BE `chore/dashboard-default-from-owner`, `feature/meeting-list-organizer`, `chore/planner-on-in-development` | 1 each | Not merged. |
| FE `feature/planner-redesign` | 18 commits | Not merged. Part of the planner redesign already reached `Development`. |
| FE `fix/nav-rail-expand-slot`, `fix/meeting-form-start-in-future` | 1 each | Not merged. |
| `claude/nice-heisenberg-lcattd` (both repos) | | The branches above merged into one branch for review. Unit and architecture tests pass on it. Not merged into the integration branches. |
