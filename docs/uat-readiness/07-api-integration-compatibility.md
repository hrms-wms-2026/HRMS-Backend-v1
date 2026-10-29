# 7. API Integration Compatibility Report

## Table of Contents

1. [Method](#1-method)
2. [Results](#2-results)
3. [Confirmed Mismatches](#3-confirmed-mismatches)
4. [False Positives](#4-false-positives)
5. [Runtime Confirmation](#5-runtime-confirmation)
6. [Tray App Integration](#6-tray-app-integration)
7. [Recommendations](#7-recommendations)

## 1. Method

- **Backend routes:** every `[Route]` and `[Http*]` attribute in `src/ONEVO.Api/Controllers`, with route parameters normalised to `{}`.
- **Frontend calls:** every `http.get/post/put/patch/delete` call with a URL literal in `src/app` (spec files excluded). Base-path fields such as `basePath` and `baseUrl` were resolved per file, and the `apiUrl` prefix was removed.
- Each call was matched on path shape and HTTP verb. Calls whose URL is built entirely from a variable are reported as unresolved.

## 2. Results

| Outcome | Count |
|---|---:|
| Matched (path and verb) | 421 |
| Unresolved (dynamic URL, not checked) | 33 |
| Path matched but verb differs | 1 (false positive) |
| No backend route | 5 (3 real, 2 false positives) |

## 3. Confirmed Mismatches

| ID | Frontend call | Backend status | Impact |
|---|---|---|---|
| UAT-001 | `GET /planner/me/range`, `GET /planner/me/focus` (`modules/work/planner/data-access/planner-api.service.ts:27,33`, added in `ec76d8e8` on 2026-10-27) | Only on `feature/planner-redesign-backend` (`PlannerController.cs:29,33` on that branch). Not on `development`. | Planner month, week and agenda views and the focus panel fail once `Planner:Enabled` is on. Verified at runtime: 404. |
| UAT-006 | `GET /org/legal-entities/{id}/positions/coverage/by-target` (`organization/data-access/position-api.service.ts:181`, used by `position.store.ts:330` → `position-coverage-modal`) | No such route. `PositionsController` has `coverage/resolve` and `{positionId}/coverage` only. | The coverage modal never shows which owners already cover the chosen target. The error is swallowed (`targetOccupancy: []`). |
| UAT-017 | `POST /employees/{id}/tray-app-access` (`people/data-access/people-api.service.ts:105`) | Never built | No user impact: no UI calls it. Dead code. |

## 4. False Positives

| Call | Why it matches |
|---|---|
| `GET /inbox/{itemPath}/export`, `POST /inbox/{itemPath}/actions/{key}` | `itemPath(ref)` expands to `{source}/{kind}/{id}`, which matches `InboxController` `{source}/{kind}/{id:guid}/export` and `/actions/{actionKey}`. |

## 5. Runtime Confirmation

Walking 21 owner routes recorded these backend errors only:

| Status | Request | Cause |
|---|---|---|
| 503 | `GET /planner/me` | Planner off (expected; this is the availability probe) |
| 404 | `GET /planner/me/range`, `/planner/me/focus` | UAT-001 |
| 401 | `GET /auth/me` before login | Expected |

The Weekly Work Progress endpoint (`/work/my-weekly-progress`), which returned 404 on older branch combinations, now returns 200 on `development`.

## 6. Tray App Integration

The backend side was reviewed statically only. Tray calls authenticate with the device JWT, and handlers take tenant and user from the token. The Tray App source and release were **out of scope**. Manual end-to-end steps are in report 11, section 5.

## 7. Recommendations

1. Merge `feature/planner-redesign-backend` into `development` before UAT, or keep `Planner:Enabled=false` in UAT and leave the planner out of scope. Branch `claude/nice-heisenberg-lcattd` already holds this merge with unit and architecture tests passing.
2. Build `coverage/by-target` or remove the frontend call.
3. Add a contract check to CI: generate OpenAPI from the backend and compare it with frontend call sites. The script used for this report could be the starting point.
