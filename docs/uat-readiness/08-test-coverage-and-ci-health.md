# 8. Automated Test Coverage and CI Health Report

## Table of Contents

1. [Test Results on Audited HEADs](#1-test-results-on-audited-heads)
2. [Test Inventory](#2-test-inventory)
3. [CI Health](#3-ci-health)
   3.1 [Backend](#31-backend)
   3.2 [Frontend](#32-frontend)
4. [Coverage Gaps](#4-coverage-gaps)
5. [Recommendations](#5-recommendations)

## 1. Test Results on Audited HEADs

| Suite | Result (2026-10-29) | Notes |
|---|---|---|
| BE Release build | Pass, 6 warnings | Nullable warnings (UAT-018) |
| BE architecture | 755/755 | |
| BE unit | 7,312/7,312 | |
| BE integration | **Not run** | No Docker in the audit container. Last reported: 560 pass, 0 fail, 1 skipped (2026-10-27). |
| FE lint | **5 errors** | UAT-013 |
| FE production build | Pass, 0 warnings | |
| FE unit (Vitest) | 5,067 pass / 6 fail | All 6 failures are in one flaky spec. It passes on its own (UAT-014). |
| FE e2e (Playwright) | **Not run** | Needs the stack and test accounts. A manual runtime walk was done instead. |

## 2. Test Inventory

| Repository | Suite | Files |
|---|---|---:|
| Backend | Unit | 981 |
| Backend | Architecture | 86 |
| Backend | Integration | 148 |
| Frontend | Unit specs | 553 |
| Frontend | Playwright e2e | present (`npm run e2e`) |

## 3. CI Health

### 3.1 Backend

`.github/workflows/ci.yml` builds, then runs unit and architecture tests on every push and PR to `main`, `testing` and `development`. On PRs it runs integration tests filtered by changed files (`select-integration-filter.ps1`), and the full suite on push and nightly. Open items: tag-pinned actions, no `permissions:` block, and the default `persist-credentials` (UAT-012).

Check the latest `development` CI run for PR #202 in GitHub Actions before UAT. This audit could not see CI results.

### 3.2 Frontend

**No CI exists.** Lint, zero-warnings build and unit tests run only when a developer runs them, or through the optional pre-push hook. The result is already visible: `Development` has 5 lint errors two days after the lint backlog was declared cleared (UAT-002, UAT-013).

## 4. Coverage Gaps

| Gap | Severity | ID |
|---|---|---|
| Negative authorization tests for the 314 handler-authorized endpoints | P2 | UAT-007 |
| Frontend-backend contract test | P2 | UAT-002 / report 7 |
| Multi-instance job behaviour (leases, double runs) | P2 | UAT-010 |
| Upgrade of an existing database | P2 | UAT-020 |
| End-to-end Tray App flows | Manual | Report 11 |

## 5. Recommendations

1. Add `.github/workflows/ci.yml` to the frontend: `npm ci`, `npm run lint`, `npm run check:warnings`, `ng test --watch=false`. Make it required on `Development`.
2. Before UAT sign-off, get one green backend integration run on the exact release commit and link it in report 12.
3. Fix the flaky landing-header spec so the new frontend CI stays reliable.
