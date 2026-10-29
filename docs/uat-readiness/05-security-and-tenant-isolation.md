# 5. Security and Tenant Isolation Report

## Table of Contents

1. [Summary](#1-summary)
2. [Tenant Isolation](#2-tenant-isolation)
   2.1 [Controls](#21-controls)
   2.2 [Runtime Cross-Tenant Probes](#22-runtime-cross-tenant-probes)
3. [Authentication and Session Security](#3-authentication-and-session-security)
4. [Authorization Coverage](#4-authorization-coverage)
5. [Input, Output and File Handling](#5-input-output-and-file-handling)
6. [Configuration and Supply Chain](#6-configuration-and-supply-chain)
7. [Results Reused from the Hardening Pass](#7-results-reused-from-the-hardening-pass)
8. [Not Covered](#8-not-covered)

## 1. Summary

**No P0 security or cross-tenant defect was found.** Tenant isolation held in every runtime probe. Authorization is enforced by attributes or inside handlers, and every sampled handler had the right check. The remaining risks are about configuration (UAT-003), weak verification (UAT-004, UAT-007) and hardening (UAT-009, UAT-012).

## 2. Tenant Isolation

### 2.1 Controls

| Control | Status | Evidence |
|---|---|---|
| PostgreSQL RLS on tenant-owned tables | Verified by tests | Architecture tests 755/755 (RLS coverage tests fail for any tenant table without a policy) |
| Runtime role is not superuser and cannot bypass RLS | Verified | `ops/postgres/local-bootstrap-roles.sql`; the API connects as `onevo_app` |
| Host-based tenant resolution | Verified | A session for `dapi` gets 401 on `acme.onexso.com` |
| `IgnoreQueryFilters()` keeps a `TenantId` predicate | Reused | Hardening report: all 21 uses checked |
| Data migrations use RLS admin mode explicitly | Verified | `20261021140000`, `20261019040520`, `20261021181949` and others call `set_config('app.tenant_context_mode','admin',true)` |

### 2.2 Runtime Cross-Tenant Probes

Signed in as the `dapi` tenant owner (full access):

| Request | Expected | Actual |
|---|---|---|
| `GET dapi…/api/v1/employees/{acme employee id}` | 404 | **404** |
| `GET dapi…/api/v1/employees/{acme employee id}/detail` | 404 | **404** |
| `GET dapi…/api/v1/work/projects/{acme project id}` | 404 | **404** |
| `GET acme…/api/v1/auth/me` with the dapi session | 401 | **401** |
| `GET acme…/api/v1/employees` with the dapi session | 401 | **401** |

## 3. Authentication and Session Security

- HttpOnly session cookie and a CSRF double-submit header. No tokens in browser storage (hardening report, still true).
- Legal consent is enforced before any session is issued for first-time users (verified at runtime).
- MFA endpoints exist (`AuthMfaController`). End-to-end MFA was **untested**.
- Demo accounts with a shared password that is visible in source exist only when the environment is Development or Test. A UAT host that runs as `Development` would expose them, and some of those accounts use real people's email addresses (`DevSmokeTestTenantSeeder.cs:83-89`). See UAT-003.

## 4. Authorization Coverage

| Category | Count |
|---|---:|
| All controller actions | 738 |
| Protected by attribute | 392 |
| Anonymous by design | 32 |
| Protected inside the handler | 314 |

Hand-reviewed handlers and the runtime employee probes are listed in report 3, section 3.2. All enforced the expected rule. The structural weakness is that handler checks have no negative integration tests (UAT-007), so a future handler without a check would not be caught.

## 5. Input, Output and File Handling

| Topic | Status |
|---|---|
| Stored XSS in task descriptions and comments | Fixed in the hardening pass (backend `RichTextRules.HasActiveContent`, frontend DOMPurify) |
| Checklist template rich text | Not validated on write (UAT-011, carried over) |
| Upload content validation | Extension and content-type only, except avatars. No malware scanning, and `FileRecord.Status` is never gated (UAT-009, SEC-13). |
| Raw SQL | None outside migrations (reused) |

## 6. Configuration and Supply Chain

| Topic | Status |
|---|---|
| Frontend runtime dependencies | `npm audit --omit=dev`: 0 vulnerabilities |
| Backend packages | Only test-only CVEs (SSH.NET, SQLitePCLRaw), carried over as P3 |
| CI workflow | Actions pinned by tag; no `permissions:` block (UAT-012, carried over) |
| CORS | Allows `{anything}.{RootDomain}` on any scheme, so `http://` subdomains also pass (`CorsExtensions.cs:28-39`). P3, UAT-019. |
| Swagger | On in Development or when `Swagger:Enabled` is set (`SwaggerExtensions.cs:44`). Keep it off in UAT. |

## 7. Results Reused from the Hardening Pass

Secret scanning (gitleaks), Semgrep, DevSkim, `dotnet list package --vulnerable`, OSV-Scanner, Trivy, actionlint and zizmor all ran on 2026-10-27/28 using kajaatharan's `../dev-env` tooling. Those tools are not available in this environment, so they were **not re-run**. Re-run them on the release candidate (report 11).

## 8. Not Covered

- DAST (OWASP ZAP) against a running stack. It needs a stable UAT URL.
- Platform Admin app and admin endpoints beyond the attribute scan (112 actions use `RequirePlatformPermission`).
- Tray App binary and its token storage on the device.
- Rate limiting and brute-force protection on login.
