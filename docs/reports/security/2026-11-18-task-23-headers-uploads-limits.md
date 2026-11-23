# Task 23: security headers, upload checks and request limits

Date: 2026-11-18. Task file: `docs/Improvements/person-1-security-and-auth/task-23 Security headers, upload checks and request limits.md`. Branch cut from `origin/development`.

**Swagger removed.** Swagger UI and every runtime `/swagger` and `/openapi` endpoint are gone in every environment, Development included. The API description is now generated at build time and committed in `docs/api/openapi/` (section 6).

## 1. Done-when checks

| Check | Shown by |
|---|---|
| A 2 MB JSON body to a normal endpoint gets 413 | `SecurityHeadersUploadsAndLimitsIntegrationTests.TwoMegabyteJsonBody_ToANormalEndpoint_Gets413` (signed in) and `..._Anonymous_Gets413BeforeAnythingReadsIt`; the live scan below (`HTTP 413`) |
| A renamed `.exe` uploaded as `.png` is rejected | `SecurityHeadersUploadsAndLimitsIntegrationTests.ExeRenamedToPng_IsRejected` (HTTP), `FileStorageServiceTests.UploadAsync_ExeRenamedToPng_IsRejectedBeforeQuotaOrStorage`, `UploadPurposePolicyTests.ValidateSignature_ExeRenamedToAnAllowedExtension_IsRejected` (11 extensions), `SkiaImageSanitizerTests.SanitizeAsync_ExeNamedPng_IsRejected` |
| The header scan shows no missing header | Live scan (section 7): every header present; HSTS is off in Development by design. `HeaderScan_NormalEndpoint/HealthEndpoint/FileDownload` integration tests check the full set, HSTS included, in the Test environment over HTTPS |

GPS EXIF removal: `SkiaImageSanitizerTests.SanitizeAsync_JpegWithGpsExif_RemovesTheExifAndKeepsAnUprightJpeg` and, end to end through upload and download, `SecurityHeadersUploadsAndLimitsIntegrationTests.UploadedPhoto_ComesBackWithoutItsGpsExif`.

## 2. Headers before and after

| Header | Before | After |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | `nosniff` |
| `X-Frame-Options` | `DENY` | `DENY` |
| `Referrer-Policy` | `no-referrer` | `no-referrer` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` (relaxed on `/swagger`) | same, no exception; file responses add `sandbox` |
| `Permissions-Policy` | missing | `accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(), geolocation=(), gyroscope=(), hid=(), magnetometer=(), microphone=(), midi=(), payment=(), picture-in-picture=(), publickey-credentials-get=(), screen-wake-lock=(), serial=(), usb=(), xr-spatial-tracking=()` |
| `Cross-Origin-Opener-Policy` | missing | `same-origin` |
| `Cross-Origin-Resource-Policy` | missing | `same-origin`; `same-site` on file responses |
| `Strict-Transport-Security` (not Development) | ASP.NET default: `max-age=2592000` (30 days), no subdomains | `max-age=31536000; includeSubDomains` (1 year). No `preload`. Still off in Development |

Why CORP is `same-site` on file responses: the web app shows avatars and logos with `<img src>` pointing at the API. Locally the app is on `https://acme.crownus.test:4200` and the API on `:7229`, which is another origin on the same site, so `same-origin` would block those images. XHR/fetch calls use CORS mode and are not affected by CORP either way. Session cookies are `SameSite=Strict`, so a cross-site page never got a signed-in file anyway; `same-site` keeps everything that works today working. The Permissions-Policy and COOP headers only affect documents served by the API, not the Angular app's own pages.

Code: `src/ONEVO.Api/Middleware/SecurityHeadersMiddleware.cs`, `Program.cs` (`AddHsts`), `src/ONEVO.Api/Filters/UserFileResponseFilter.cs`.

## 3. Request body limits

Default for every endpoint: **1 MB** (`RequestBodySizeLimitMiddleware`, right after `UseRouting`). An oversized `Content-Length` gets 413 before tenant resolution, authentication or anything else reads the body. For chunked bodies, Kestrel's per-request limit is set to the same value, and `ExceptionHandlerMiddleware` turns Kestrel's 413 into a problem response. Kestrel's server-wide `MaxRequestBodySize` is also 1 MB as a backstop. An endpoint's own `[RequestSizeLimit]` wins. A unit test (`EveryActionThatAcceptsAFile_DeclaresItsOwnRequestSizeLimit`) fails if any action that binds an `IFormFile` has no limit.

| Endpoint | Limit | Change |
|---|---|---|
| `POST api/v1/files` (all upload purposes) | 26 MiB | unchanged |
| `POST api/v1/leave/requests/documents` | 25,000,000 bytes | unchanged (slightly under the 25 MiB purpose cap; left as is, never lowered) |
| `POST .../check-in/face-preview` | 6 MiB | unchanged |
| `POST .../check-in/face-enroll` (3 photos) | 16 MiB | unchanged |
| `POST .../check-in/{id}/face-scan` | 6 MiB | unchanged |
| Tray `screenshots/upload`, `screenshots`, `inactivity-attempts` | 10 MiB each | unchanged |
| Tenant and admin support ticket attachments | 26 MiB | unchanged |
| `POST api/v1/work/objectives/{id}/assets` | 26 MiB | **new** (25 MiB documents) |
| `POST api/v1/work/tasks/pending-uploads` | 26 MiB | **new** (25 MiB task/comment attachments) |
| `POST api/v1/work/projects` (multipart, optional logo and banner) | 11 MiB | **new** (5 + 5 MiB images) |
| `POST api/v1/onboarding/bulk-batches` | 6 MiB request, 5 MiB file | **new**. Max 200 rows (`CsvBatchParser.MaxRows`), so 5 MiB is generous |
| `PUT admin/v1/app-catalog/{id}/logo` | 3 MiB | **new** (handler allows 2 MiB) |
| `POST/PUT admin/v1/legal-document-versions` | 4 MiB | **new**: JSON, but the same document goes three times (JSON, HTML, text) |
| Stripe webhooks (both routes) | 2 MiB | **new**: headroom, as a failed webhook retries forever |
| Everything else | 1 MB | **new** default (was Kestrel's ~30 MB) |

I searched for other large JSON bodies (base64 images, imports, tray batches) and found none that need more: tray ingest batches are capped at 200 small snapshots, and no endpoint takes base64 images. The limits are in `src/ONEVO.Api/Configuration/RequestBodyLimits.cs`.

## 4. Upload checks

Shared path: every tenant upload goes through `FileStorageService.UploadAsync`, which now does all of this before any quota or storage work:

1. Allow-listed extension and Content-Type for the purpose, plus size (`UploadPurposePolicy.ValidateUpload`).
2. Real file signature (magic bytes) of the bytes as sent (`ValidateSignature`). It now covers every allowed type, including the chat video and audio formats and CSV, which used to pass unchecked. An extension with no check is refused (fail closed).
3. PNG, JPEG and WebP are re-encoded (section 5). Avatars keep their own 256 px WebP processor.
4. The stored object name is generated on the server: `tenants/{tenant}/{purpose}/{reservation}/content{.ext}`. The client's name is kept only for display (`original_file_name`/`safe_file_name`). Existing files keep their old keys.

The three paths outside `FileStorageService` use the same pieces: support ticket attachments (`SupportAttachmentService`: same policy, re-encoding, server-generated key), the app catalog logo (its own signature check plus re-encoding), and bulk onboarding (signature check; the file is parsed, never stored).

Allowed types per endpoint (from `UploadPurposeCatalog`):

| Endpoint / purpose | Allowed |
|---|---|
| `files`: `employee_avatar`, `company_logo`, `project_cover`, `project_banner`, `conversation_avatar`, `task_description_image`, `comment_description_image`, `monitoring_face_scan`, `biometric_reference_photo` (5 MiB) | PNG, JPEG, WebP |
| `files` / tray: `monitoring_screenshot` (10 MiB) | PNG, JPEG, WebP |
| Check-in face preview/enroll/scan | PNG, JPEG, WebP (by purpose above) |
| `files`: `generic_document`, `leave_supporting_document` (25 MiB), `employee_document` (10 MiB); leave documents endpoint | PDF, PNG, JPEG, DOC, DOCX |
| `objective_asset` (25 MiB) | PDF, PNG, JPEG, GIF, DOC, DOCX, XLS, XLSX, ZIP |
| `task_attachment`, `comment_attachment`, `support_ticket_attachment` (25 MiB) | PDF, PNG, JPEG, WebP, GIF, DOC, DOCX, XLS, XLSX, ZIP |
| `conversation_attachment` (25 MiB) | the task attachment list plus MP4, WebM, MOV, MP3, WAV, M4A, OGG |
| Bulk onboarding | CSV (text, no NUL bytes or binary headers), XLSX (zip) |
| App catalog logo (2 MiB) | PNG, JPEG, WebP, GIF |

Serving files: every `FileResult` from the API (`UserFileResponseFilter`, global) gets `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; sandbox` and `Content-Disposition: attachment`. Named downloads (CSV/XLSX exports, templates) keep MVC's `attachment; filename=...`. **Inline is allowed only for `image/png`, `image/jpeg` and `image/webp`.** That is safe: those types cannot run script, nosniff stops the browser treating them as anything else, the sandbox CSP applies if one is opened in a tab, and new uploads of those types are re-encoded. GIF, SVG (never allowed), PDF, Office files and everything else download. Support attachments are served from R2 signed URLs, not the API origin, so they are unchanged.

## 5. Image re-encoding

`SkiaImageSanitizer` (SkiaSharp) decodes the image and writes back only the pixels, in the same format: PNG stays lossless, JPEG and WebP use quality 90. This drops EXIF (GPS position, camera, time), XMP, comments and any bytes hidden after the image data. The EXIF orientation is baked into the pixels, so phone photos stay upright. It applies to check-in face scans and reference photos (the privacy case), screenshots, logos, covers and banners, chat photos and image attachments. The decoded format must match the extension, and images over 16,384 px on a side or 40 million pixels are refused before decoding (decompression-bomb guard; three 4K monitors side by side fit). GIF is not re-encoded, because SkiaSharp would drop the animation; GIFs are always served as attachments.

## 6. Swagger removed, build-time OpenAPI documents

- Removed: `SwaggerExtensions.cs`, `Swashbuckle.AspNetCore`, the `Swagger:Enabled` setting (`appsettings.Development.json`, `.env.example`), the `/swagger` CSP exception, and the `Microsoft.OpenApi` pins in the three test projects.
- Added: `Microsoft.AspNetCore.OpenApi` 10.0.10 plus `Microsoft.Extensions.ApiDescription.Server` 10.0.10. `AddOpenApi` registers the documents and nothing maps them: no `MapOpenApi`, no `UseSwagger`.
- Every `dotnet build` writes the documents to `src/ONEVO.Api/obj/openapi/`. The committed copies:

| Document | Path | Paths | Operations | Typed 2xx response | Security schemes |
|---|---|---|---|---|---|
| Tenant API (everything except `admin/v1`) | `docs/api/openapi/tenant-v1.json` | 541 | 629 | 0 | `TenantSessionCookie` (`crownus_session`), `CsrfToken` (`X-CSRF-Token`), `TrayDeviceBearer` (JWT) |
| Admin API (`admin/v1`) | `docs/api/openapi/admin-v1.json` | 123 | 151 | 4 | `AdminSessionCookie` (`admin_session`), `CsrfToken` |

- No database and no secrets needed: when running under the generator (`GetDocument.Insider`), `Program.cs` skips the config and database startup checks, migrations and the dev cert, adds placeholder connection strings, and removes the hosted services (seeders) before `Build()`. Verified: the documents build in this worktree with no `.env` and no database connection, and two builds produce byte-identical files.
- Drift check: `OpenApiDocumentsAreCurrentTests` (unit) fails when `docs/api/openapi/*.json` differ from the build output. To refresh: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -p:UpdateOpenApiDocs=true`.
- Runtime check: `ApiBootTests.SwaggerAndOpenApiPaths_ServeNothing` (Test and Development, even with `Swagger:Enabled=true`) checks that `/swagger`, `/swagger/index.html`, `/swagger/v1/swagger.json`, `/swagger/admin-v1/swagger.json`, `/openapi/v1.json` and `/openapi/admin-v1.json` get 401 or 404.
- Docs updated: `docs/postman-request/README.md` (Import → File), `docs/api/openapi/README.md`, `docs/deployment/rate-limits.md`, `docs/superpowers/plans/next/2026-11-09-crownus-vm-deployment.md`, `docs/superpowers/specs/next/2026-11-17-admin-managed-rate-limits-guide.md`. `docs/api/public-endpoints.md` does not exist on `development` yet (task 15 adds it), so its Swagger lines need the same edit when tasks 15 and 23 meet.

### Response-type gaps

Almost every action returns `IActionResult` with no `[ProducesResponseType]`, so 625 of 629 tenant operations and 147 of 151 admin operations have no 2xx response schema (request bodies and parameters are typed). The only typed responses are the 4 `GET admin/v1/modules/catalog...` operations. Biggest gaps by controller: tenant Employees 51, Tasks 43, Calendar 35, Meetings 25, Objectives 22, Positions 20, InboxChats 16, Projects 15, TimeTracking 14, Conversations 13, Planner 12, Sprints 11; admin PlatformAccess 14, AdminAuth 11, AdminSupportTickets 11, Tenants 9, AdminNotificationTemplates 9. Fixing these (adding `[ProducesResponseType<T>]` or typed results) is a follow-up; the frontend's TypeScript types will lack response models until then.

## 7. Header scan (local)

Run with `curl -sk -D -` against the running API; no external scanner. "Before" is the main checkout's API on :7229, "after" is this branch on :7339, both in Development (so HSTS is absent by design). `/health` on a tenant host answers 401 without a session; the headers are what matter here.

Before (each of `/api/v1/auth/me`, `/health`, `/api/v1/files/{id}` printed the same set):

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
MISSING: strict-transport-security
MISSING: permissions-policy
MISSING: cross-origin-opener-policy
MISSING: cross-origin-resource-policy
```

After (same for `/api/v1/auth/me`, `/health`, `/api/v1/files/{id}`, `/swagger/index.html` and `/openapi/v1.json`, all 401):

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
Permissions-Policy: accelerometer=(), autoplay=(), camera=(), ... xr-spatial-tracking=()
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-origin
MISSING: strict-transport-security   (Development only; UseHsts is skipped there)
2 MB JSON to POST /api/v1/auth/login -> HTTP 413 {"type":"urn:crownus:error:payload-too-large",...}
```

A signed-in file download was not scanned live: the dev seed users have real people's email addresses, and signing in as them could send them mail. The integration test `HeaderScan_FileDownload_IsServedSafely` checks it through the real pipeline instead: HSTS, nosniff, `Content-Disposition: inline` for a PNG, `sandbox` CSP and CORP `same-site`.

**The UAT scan has to wait until this is deployed.** Run the same script against `https://<tenant>.crownus.test` there; HSTS should then show `max-age=31536000; includeSubDomains`.

## 8. Behaviour changes the frontend may notice

- **413** with `application/problem+json` (`type: urn:crownus:error:payload-too-large`) for any JSON body over 1 MB, and for uploads over the limits above. CORS headers are on the 413, so the app can read it.
- **Rejected file types (400):** bytes that don't match the extension (renamed files), fake or corrupt images, CSV files that are not text (UTF-16 CSV contains NUL bytes and is refused, so save as UTF-8), and video/audio whose header doesn't match. Images over 16,384 px or 40 MP are refused.
- **Stored images are re-encoded:** same format and dimensions, but different bytes and usually a different size (a checksum shown to users would change). No metadata is kept.
- **Download vs inline:** PDFs, Office files, ZIPs, GIFs and media from `/api/v1/files/{id}` and the other file endpoints now come with `Content-Disposition: attachment`. Opening one in a new tab downloads it instead of showing it. `<img>` tags, and blobs fetched with XHR, are unaffected. PNG/JPEG/WebP stay inline.
- **CORP:** API responses are `same-origin`, file responses `same-site`. An `<img>` or `<video>` on a different site (not `*.crownus.test` / the production domain) pointing at an API file would now be blocked. Same-site use, including local `:4200` → `:7229`, works.
- **Swagger is gone everywhere.** Use `docs/api/openapi/*.json` (Postman Import → File) or the hand-made collection.

## 9. Verification

See the commit message and the gate output for the final numbers. During development: unit 8082/8082, architecture 713/713, and the targeted integration run (new tests, ApiBootTests, LegalEntities, BulkOnboarding) 73/74. The one failure was the `/health` test without a session cookie; it was fixed and re-run in the full gate.
