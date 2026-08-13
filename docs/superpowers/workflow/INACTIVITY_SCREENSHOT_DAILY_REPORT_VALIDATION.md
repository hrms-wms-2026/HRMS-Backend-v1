# Inactivity Screenshot Daily Report — Validation Record

**Date:** 2026-08-13  
**Plan:** `docs/superpowers/plans/2026-08-10-inactivity-screenshot-daily-report.md`

## Automated Test Matrix

### Tray (`C:\HR\tray_app_maui`)

| Command | Result |
|---------|--------|
| `dotnet test tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj -c Release` | **18/18 pass** |
| `dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -c Release` | **33+ pass** (inactivity/capture/notification filters) |
| `dotnet test tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj -c Release` | **102/102 pass** |

### Backend (`C:\HR\HRMS-Backend-v1`)

| Command | Result |
|---------|--------|
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj -c Release --filter SubmitInactivityCaptureAttempt` | **27/27 pass** |
| `dotnet test tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj -c Release --filter "MonitoringWorkSessionCompleted\|GetEmployeeDailyMonitoringReport\|ActivityDailySummaryAggregator"` | **10/10 pass** |
| `dotnet test tests\ONEVO.Tests.Architecture\ONEVO.Tests.Architecture.csproj -c Release --filter InactivityCaptureAttempt` | **3/3 pass** |
| `dotnet build src\ONEVO.Api\ONEVO.Api.csproj -c Release` | **Build succeeded** |
| `dotnet test tests\ONEVO.Tests.Integration\ONEVO.Tests.Integration.csproj --filter InactivityCaptureIngestIntegrationTests` | **7/7 pass** (Docker + Testcontainers) |

## API Endpoints

| Method | Route | Auth |
|--------|-------|------|
| GET | `/api/v1/monitoring/tray/policy` | Tray Device JWT |
| POST | `/api/v1/monitoring/tray/inactivity-attempts` | Tray Device JWT (multipart) |
| GET | `/api/v1/monitoring/activity/daily-report?employeeId=&date=` | Tenant JWT + `monitoring:read` |
| GET | `/api/v1/monitoring/screenshots/{id}/url` | Tenant JWT + `monitoring:read` (signed URL) |

## Manual Smoke Checklist (Windows + two monitors)

- [ ] Clock in → idle 300s → Allow → one combined JPEG ≤ 10 MB
- [ ] Service SQLite row pending until backend 200; encrypted spool deleted after ack
- [ ] PostgreSQL: one `inactivity_capture_attempts` row + one `monitoring_evidence_assets` row; no image bytes in DB
- [ ] R2: one private object via `FileRecordId`
- [ ] Daily report: `CapturedCount=1`, evidence asset ID present, no permanent URL
- [ ] Skip / timeout / break / clock-out / policy disable: metadata only, no image
- [ ] Offline: attempt stays pending; work session does not overtake failed attempt

## Privacy Checks

- No screenshot bytes, base64, object keys, or permanent URLs in logs or SQLite JSON payloads
- Tray IPC envelopes stay under 65,536 characters per message
- Local evidence DPAPI-protected under `%ProgramData%\ONEVO\Agent\EvidenceSpool`
