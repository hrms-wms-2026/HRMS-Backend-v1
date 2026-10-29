# 13. Go/No-Go Recommendation

## Table of Contents

1. [Recommendation](#1-recommendation)
2. [Conditions for Go](#2-conditions-for-go)
3. [Accepted Risks for UAT](#3-accepted-risks-for-uat)
4. [Rationale](#4-rationale)

## 1. Recommendation

**NO-GO for starting UAT on the current commits. Change to GO once the four conditions below are met.** Based on the evidence, they can be closed in a short focused effort. Three of them are mostly agent-fixable, and one needs an environment with Docker.

## 2. Conditions for Go

| No. | Condition | Issue | Owner |
|---|---|---|---|
| 1 | Merge the planner redesign backend into `development`, **or** declare the planner out of UAT scope with `Planner__Enabled=false` | UAT-001 | Product decision, then the developer or an agent |
| 2 | Frontend CI exists and is green on the UAT commit (5 lint errors fixed) | UAT-002, UAT-013 | Developer or agent; repository admin for branch protection |
| 3 | UAT environment runbook approved. API runs as non-Development with `Tenancy__RootDomain` and HTTPS origins set. Swagger and demo seeders off. | UAT-003 | DevOps and lead |
| 4 | Full backend integration suite green on the exact UAT commit | UAT-004 | Developer (Docker) |

## 3. Accepted Risks for UAT

These are acceptable for a controlled UAT if the team agrees in writing:

| Issue | Proposed treatment |
|---|---|
| UAT-009 No malware scanning | Accept for UAT with internal test users and test files only. |
| UAT-010 Jobs without leases | Run UAT with **one** API instance. |
| UAT-007 No authorization-matrix tests | Accept. Runtime probes and handler review found no gaps. Add the tests during UAT. |
| UAT-006 Coverage-by-target | Accept with a known-issue note, or fix before UAT if org coverage is tested in the first UAT cycle. |
| UAT-020 No upgrade rehearsal | Accept only if the UAT database is created fresh. Otherwise rehearse first. |
| UAT-021 Tray end to end not tested | Make it the first UAT test cycle. |

## 4. Rationale

- **For Go:** no P0 found; tenant isolation verified at runtime; 755 architecture and 7,312 unit tests green; 268 migrations apply to a fresh database; a production frontend build with zero warnings; the main screens load for owner and employee roles.
- **Against Go today:** a visible feature mismatch (planner); no automated gate on the frontend, and that gate has already regressed; the deployment configuration could make UAT either fail or expose demo accounts; the integration-level evidence is two days and several merges old.

The application is **not** declared UAT-ready by this audit. Re-assess after the conditions in section 2 are met.
