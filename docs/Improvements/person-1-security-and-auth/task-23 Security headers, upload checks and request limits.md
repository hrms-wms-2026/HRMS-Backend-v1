# Task 23: Security headers, upload checks and request limits

| | |
|---|---|
| Priority | 23 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security |
| Depends on | nothing |

## What we found

- `SecurityHeadersMiddleware` sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and a deny-all CSP. It does not set `Permissions-Policy`, `Cross-Origin-Opener-Policy` or `Cross-Origin-Resource-Policy`.
- `UseHsts()` runs with defaults (30 days, no subdomains). Tenants live on subdomains.
- There is no global request body limit in `Program.cs`. Only 8 actions set `[RequestSizeLimit]`. Every other POST accepts the Kestrel default of about 30 MB.
- File endpoints (`FilesController`, leave attachments, screenshots, check-in photos) set a size limit. Check that each one also checks the real file type, not just the extension or the `Content-Type` sent by the client.
- Swagger can be switched on by a setting in any environment.

## What to do

1. Add the missing headers. Set HSTS to one year with `includeSubDomains`.
2. Set a small global body limit (for example 1 MB) and keep larger limits only on the upload actions.
3. For every upload: allow-list of types, check the file signature bytes, generate the stored file name on the server, never serve user files from the API origin without `Content-Disposition: attachment` and `nosniff`.
4. Re-encode uploaded images with SkiaSharp (already referenced) to strip metadata and hidden content.
5. Put Swagger behind admin sign-in when enabled outside Development.
6. Run a header scan against the UAT URL and save the result.

## Done when

- A 2 MB JSON body to a normal endpoint gets HTTP 413.
- A renamed `.exe` uploaded as `.png` is rejected.
- The header scan shows no missing header.

## Status (2026-11-19)

Built and verified, in review: PR #240 (`feature/testing` to `development`, together with tasks 15, 19 and 27), not merged yet. Report: `docs/reports/security/2026-11-18-task-23-headers-uploads-limits.md` (arrives with PR #240).

- Step 5 changed: Swagger was removed in every environment instead of being put behind admin sign-in. The API description is generated at build time and committed as `docs/api/openapi/tenant-v1.json` and `admin-v1.json` (see `docs/api/openapi/README.md`). The team uses Postman.
- Step 6: the header scan was run on the local host. Run it again against the UAT URL once UAT is up.
