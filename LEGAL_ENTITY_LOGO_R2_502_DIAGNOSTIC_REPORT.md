# Legal Entity Logo Upload — 502 `file_upload_failed` Diagnostic Report

**Repo:** `C:\onevoNew\HRMS-Backend-v1`
**Scope:** Diagnose and fix `PUT /api/v1/org/legal-entities/{id}/logo` returning 502
`file_upload_failed` after the storage-quota entitlement gap (`storage_not_entitled`) was
already fixed (see `STORAGE_QUOTA_LOCAL_LOGO_UPLOAD_FIX_REPORT.md`, uncommitted in this
working copy).

---

## 1. Root Cause

There are **two independent causes** stacked behind the single 502, confirmed by directly
reproducing `CloudflareR2ObjectStorageAdapter.PutObjectAsync`'s exact code path (same
`AmazonS3Config`, same decrypted credential bundle read from the live `platform_service_keys`
row, same `PutObjectRequest` shape) against the real Cloudflare R2 bucket:

### 1a. Code bug (fixed in this session) — AWS SDK v4 default streaming is incompatible with R2

`AWSSDK.S3 4.0.101.3`'s default `PutObjectRequest` behavior chunks the upload body and signs
it with `STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER` (a flexible-checksum trailer introduced
as the SDK's new default). **Cloudflare R2 does not implement this signing scheme.** Every
`PutObject` call — regardless of how correct the credentials are — failed with:

```
StatusCode: NotImplemented (501)
ErrorCode:  NotImplemented
Message:    STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not implemented
```

Disabling only the request-level checksum trailer still left chunked signing on and produced
the same class of error one layer down:

```
StatusCode: NotImplemented (501)
ErrorCode:  NotImplemented
Message:    STREAMING-AWS4-HMAC-SHA256-PAYLOAD not implemented
```

Only forcing **non-chunked** upload (`PutObjectRequest.UseChunkEncoding = false`) removes the
chunked/streaming signature entirely and lets the SDK send a plain, upfront-signed, `Content-
Length`-known body — which R2 does support. This one setting was sufficient by itself (no
additional client-level checksum config was needed once chunking was off).

This bug affects **every** tenant, **every** upload purpose (`company_logo`, `employee_avatar`,
etc.) — it is not tenant-specific, and it existed as soon as R2 was wired up, independent of
the quota fix. The quota fix simply removed the earlier `storage_not_entitled` short-circuit
that was hiding it.

**This is the part fixed by this session's code change.**

### 1b. External configuration gap (not fixed in this session, requires user action)

Once the streaming incompatibility above was bypassed, the exact same request (same
credentials, same bucket, replayed against the real R2 endpoint) still failed:

```
StatusCode: Forbidden (403)
ErrorCode:  AccessDenied
Message:    Access Denied
```

...on **both** `PutObject` and `DeleteObject`. A parallel `GetObjectMetadata` (HEAD) probe
against the same bucket succeeded (404 for a nonexistent key — i.e., the request reached R2
and was authorized for read). This proves:

- The credential bundle itself is valid and correctly shaped (see §2) — the identity
  authenticates fine (no `SignatureDoesNotMatch`/`InvalidAccessKeyId`).
- The R2 API token currently stored in `platform_service_keys` (`cloudflare_r2`) has
  **read-only** permission on bucket `onexso`. It does **not** have object write/delete
  permission.

**This cannot be fixed in code.** It requires either regenerating the Cloudflare R2 API token
with "Object Read & Write" permission scoped to bucket `onexso`, or rotating the stored
`platform_service_keys` row (`RotatePlatformServiceKeyCommand`) with a new token that has that
permission, via the Cloudflare dashboard.

---

## 2. Required Investigation Checklist

| # | Item | Result |
|---|------|--------|
| 1 | Active `cloudflare_r2` service key exists and is active | ✅ Confirmed via `platform_service_keys` — one row, `service_key='cloudflare_r2'`, `is_active=true`. `last_verified_at` is `NULL` (harmless — see §3). |
| 2 | Decrypted bundle contains accountId/bucketName/accessKeyId/secretAccessKey/endpoint/region | ✅ All 6 fields present. Decrypted (AES-GCM, `AesEncryptionService`, local scratch tool only — never logged, never written to a persisted file) and structurally validated: `accountId` (32 chars), `bucketName='onexso'`, `accessKeyId` (32 chars), `secretAccessKey` (64 chars), `endpoint` present, `region='auto'`. |
| 3 | Endpoint format `https://{accountId}.r2.cloudflarestorage.com`, no bucket path | ✅ `https://e99bb9ab479f01fac45d7cb5a1b61372.r2.cloudflarestorage.com` — exact match, no `/onexso` suffix. |
| 4 | `bucketName` exactly matches Cloudflare bucket name `onexso` | ✅ Confirmed `bucketName == "onexso"`. |
| 5 | R2 token has object write/read/delete permission on that bucket | ❌ **Read-only.** `PutObject` and `DeleteObject` both return `403 AccessDenied`; `GetObjectMetadata` (read) succeeds. **Action required from the user in the Cloudflare dashboard — see §1b.** |
| 6 | Backend restarted after rotating/fixing the key (adapter caches client+bucket after first resolution) | ✅ Verified the caching field (`_client`/`_bucketName`) is instance-level, and `IObjectStorageAdapter` is registered `AddScoped` (`DependencyInjection.cs:226-228`) — a **new adapter instance is created per HTTP request**, so the cache never survives across requests in the first place; a stale key inside a single already-running process is not the failure mode here regardless. The running dev backend (PID 40952, started 11:16 AM) was already newer than the last `platform_service_keys` update (10:28 AM), so this was not the cause. Still restarted the backend as part of verification (see §5). |
| 7 | Backend logs for the underlying `AmazonS3Exception` status/code/message | ⚠️ **Not available before this fix** — the adapter discarded the exception entirely (`catch (AmazonS3Exception) { throw new ObjectStorageException("...") }`), so nothing about the real R2 response ever reached the logs. Fixed in this session (see §3). The actual status/code, obtained by direct reproduction, is documented in §1a/§1b above. |

---

## 3. Fix Applied

**File:** `src/ONEVO.Infrastructure/ExternalServices/Storage/CloudflareR2/CloudflareR2ObjectStorageAdapter.cs`

1. **`PutObjectAsync` now sets `UseChunkEncoding = false`** on the `PutObjectRequest` — this is
   the actual fix for the 502. Without it, the upload can never succeed against R2 regardless
   of credentials or permissions.
2. **Every `catch (AmazonS3Exception ex)` block now logs sanitized diagnostic detail** before
   wrapping and rethrowing as `ObjectStorageException`: `StatusCode`, `ErrorCode`, `RequestId`,
   the sanitized endpoint **host** (`_endpointHost`, parsed once at client-resolution time —
   never the full credential bundle), and the bucket name. The access key and secret key are
   never read into this log call. The wrapped `ObjectStorageException` now also carries the
   original `AmazonS3Exception` as `InnerException` (the exception type already supported this
   constructor; it just wasn't being used) so the full exception chain is available to whatever
   logging sink the app uses, without changing the safe, generic message that reaches
   `FileStorageService` → the 502 `file_upload_failed` response body.
3. Added `ILogger<CloudflareR2ObjectStorageAdapter>` as a constructor dependency (resolved
   automatically — DI registration is `services.AddScoped<IObjectStorageAdapter,
   CloudflareR2ObjectStorageAdapter>()`, no explicit factory to update).

**No frontend changes were made.** `file_upload_failed`/502 was already a safe, generic
message with no internal detail to sanitize further.

**No quota or object-storage bypass was introduced.** The fix is entirely inside the R2 PUT
request configuration and logging; the quota-reservation-before-upload flow in
`FileStorageService` is untouched.

---

## 4. What Was Explicitly Not Fixed (External, Out of Code Scope)

**The stored `cloudflare_r2` R2 API token only has read permission on bucket `onexso`.** Until
this is corrected in the Cloudflare dashboard (grant "Object Read & Write" on that token/bucket,
or issue a new token with that scope and rotate it in via `RotatePlatformServiceKeyCommand`),
`PUT /api/v1/org/legal-entities/{id}/logo` will continue to return 502 `file_upload_failed` —
but now for the *correct*, fully-diagnosable reason (`AccessDenied`, visible in backend logs
per the §3 logging fix), not the SDK-compatibility bug this session fixed.

Separately (lower priority, noted for completeness): `VerifyPlatformServiceKeyCommandHandler` →
`PlatformServiceKeyVerificationService.VerifyAsync` is an explicit **Phase 1 stub** — it only
checks `apiKeyPlaintext.Length >= 8` for `cloudflare_r2`, with **no live call to R2**. This is
why `last_verified_at` was `NULL` and why "verified" would not have caught either the
streaming-encoding bug or the read-only-token permission gap. A future delivery replacing this
stub with a real R2 `HeadBucket`-style check (as the file's own doc comment already flags)
would have surfaced §1b immediately without needing manual reproduction. Not implemented here —
out of this task's stated scope (adapter fix + logging + tests).

---

## 5. Tests Run

```
dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj   → Build succeeded, 0 errors
dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj         → Build succeeded, 0 errors (pre-existing warnings only)
dotnet build src/ONEVO.Api/ONEVO.Api.csproj                         → Build succeeded, 0 errors (1 pre-existing warning)

dotnet test tests/ONEVO.Tests.Unit --filter
  "FullyQualifiedName~CloudflareR2ObjectStorageAdapterTests|FullyQualifiedName~FileStorageServiceTests|FullyQualifiedName~StorageQuotaServiceTests|FullyQualifiedName~DevSmokeTestTenantSeederTests"
  → 70/70 passed

dotnet test tests/ONEVO.Tests.Unit (full suite)                     → 1989/1989 passed
  (1984 baseline from the already-uncommitted quota fix + 5 new: 4 in
  CloudflareR2ObjectStorageAdapterTests + 1 strengthened assertion added to the existing
  UploadAsync_ObjectStorageFailure_ReleasesReservationAndDoesNotComplete)

git diff --check                                                    → clean, no output
```

### New/updated test coverage

- `tests/ONEVO.Tests.Unit/Features/Storage/File/CloudflareR2ObjectStorageAdapterTests.cs` (new)
  - `PutObjectAsync_NoActiveServiceKey_ThrowsObjectStorageExceptionWithoutNetworkCall`
  - `PutObjectAsync_MissingBundleField_ThrowsObjectStorageException`
  - `PutObjectAsync_UsesNonChunkedEncoding_ForCloudflareR2Compatibility` — locks in the
    `UseChunkEncoding = false` fix so a future AWS SDK upgrade can't silently reintroduce
    chunked streaming.
  - `ObjectStorageException_FromAmazonS3Exception_NeverLeaksCredentials` — constructs an
    `AmazonS3Exception` shaped exactly like the real `AccessDenied` failure reproduced in §1b
    and proves the wrapped `ObjectStorageException`'s message and `.ToString()` never contain
    the access key or secret key.
  - `ValidBundleJson_MatchesRequiredCredentialBundleContract` — structural guard for the §2
    checklist (endpoint format, no bucket path embedded in endpoint).
- `tests/ONEVO.Tests.Unit/Features/Storage/File/FileStorageServiceTests.cs` (strengthened)
  - `UploadAsync_ObjectStorageFailure_ReleasesReservationAndDoesNotComplete` now asserts
    `result.Error == "file_upload_failed"` and `result.StatusCode == 502` explicitly (it
    previously only asserted generic failure), and asserts the reservation was never
    atomically completed — proving the exact contract this task required, not just "some
    failure happened."

### Manual reproduction (in place of a live end-to-end HTTP test — see §6 for why)

Using a standalone scratch console app (net9.0, `AWSSDK.S3` 4.0.101.3, deleted after use) that
decrypted the real `platform_service_keys` `cloudflare_r2` row with the real
`Encryption__MasterKey` and replayed the adapter's exact request shape against the real R2
endpoint:

- Before fix (default `PutObjectRequest`): `501 NotImplemented` /
  `STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not implemented`.
- With `UseChunkEncoding = false` (the applied fix): `403 AccessDenied` — the SDK-compatibility
  bug is gone; only the external token-permission gap (§1b/§4) remains.
- `GetObjectMetadata` (read): succeeds (404 for a nonexistent key) — confirms read permission
  and confirms the credential bundle authenticates correctly.
- `DeleteObject`: `403 AccessDenied` — confirms the token is read-only, not write-capable.

No object was left behind in the bucket (every write attempt was rejected before creating
anything). No secrets were written to any persisted file; the scratch tool and its output were
deleted at the end of this session.

---

## 6. Skipped Checks

- **Live end-to-end `PUT /api/v1/org/legal-entities/{id}/logo` HTTP call was not performed.**
  The tenant-host login flow requires a trusted-cert subdomain session (`acme.localhost`,
  cookie-based, workspace-selection step) that is significant additional setup, and — more
  importantly — **it would deterministically fail at the R2-permission step (§1b) regardless of
  this session's fix**, since that requires an external Cloudflare dashboard change this session
  cannot make. The direct SDK-level reproduction in §5 replays the exact same request the
  controller action would issue (same decrypted credential bundle, same `AmazonS3Config`, same
  `PutObjectRequest` shape after the fix) and is equally conclusive for the code-level bug
  without requiring that external fix first. Once the R2 token permission is corrected, the
  existing `LegalEntitiesIntegrationTests.cs` (Docker/Testcontainers-gated, not run this
  session — same reason as the prior quota-fix report: no Docker exercised) and a live manual
  upload are the appropriate follow-up verification.
- **Integration tests** (`tests/ONEVO.Tests.Integration`) were not run — Docker/Testcontainers
  was not exercised this session, consistent with the prior quota-fix report.
- **`PlatformServiceKeyVerificationService` was not upgraded** to perform a live R2 check — see
  §4; flagged as a follow-up, not implemented here since it was outside the stated fix scope.

---

## 7. Remaining Risks

1. **The 502 will persist until the R2 API token's permissions are corrected.** This is an
   external, dashboard-side action for the user — grant "Object Read & Write" on the token
   currently stored for `cloudflare_r2` (bucket `onexso`), or generate a new token with that
   scope and rotate it in via the existing `RotatePlatformServiceKeyCommand` endpoint. This
   report's fix makes that the *only* remaining blocker — backend logs will now show
   `StatusCode=Forbidden ErrorCode=AccessDenied` clearly once it's hit again, instead of a
   silent generic failure.
2. **`PlatformServiceKeyVerificationService.VerifyAsync` is still a length-only stub for all
   providers** (`Resend`, `SendGrid`, `Cloudflare`, `CloudflareR2`, `AwsRekognition`) — "verified"
   in the admin UI does not mean the credential actually works against the live provider. This
   pre-dates this session and is unrelated to the specific 502 bug, but it's why neither of the
   two root causes in this report was caught by verification before reaching production code
   paths.
3. **This fix (non-chunked upload) trades a small amount of upload efficiency for R2
   compatibility** — the whole object body is buffered and signed upfront rather than streamed
   in chunks. `FileStorageService.UploadAsync` already buffers the entire file into a
   `MemoryStream` before calling `PutObjectAsync` (see `FileStorageService.cs:320-325`), so this
   has no additional memory-usage impact in the current code path; it would matter if a future
   change moved to true streaming uploads of very large files directly from the request body.
4. **The dev backend process was stopped and restarted during this session** (it was locking
   `ONEVO.Infrastructure.dll`, blocking the test build) — it is back up on
   `https://localhost:7229` with the fix applied and started cleanly with no new startup errors.
