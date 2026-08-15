# Employee Monitoring Dashboard Design

## Goal

Build the backend product slice needed for a Live Employee Monitoring Dashboard MVP: daily productivity metrics, top app categories, employee status, and rule-based alerts exposed through tenant-safe monitoring APIs.

## Existing Foundation

The workspace already has these monitoring pieces:

- Windows Tray App collectors in `tray_app_maui/ONEVO.Agent.TrayApp/Collectors`.
- Agent Service sync routes in `tray_app_maui/ONEVO.Agent.Service/Api/AgentApiRoutes.cs`.
- Backend ingest APIs for activity, app usage, device state, work sessions, policy, screenshots, and tray activation.
- Backend daily activity summary entities, repositories, and report endpoints under `HRMS-Backend-v1/src/ONEVO.Application/Features/Monitoring`.

This design extends the backend only. A separate HR web frontend is not present in this workspace, so this build ends with dashboard-ready APIs and DTOs.

## Scope

In scope:

- Categorize foreground applications as productive, meeting, personal, or neutral.
- Populate `ActivityDailySummary` app metrics from activity snapshots.
- Expose a manager dashboard API with employee status, daily score, active/idle minutes, top apps, and alerts.
- Add a pure alert evaluation service for late login, early logout, long idle, low activity score, and low data coverage.
- Preserve existing tenant, permission, and employee visibility rules.

Out of scope:

- New Windows agent collectors.
- New database tables for app categories or alert persistence.
- Frontend dashboard screens.
- Screenshot or keystroke-content monitoring.

## Privacy And Security

- Keyboard/mouse data remains counts only.
- Raw window titles remain excluded from DTOs and persisted summaries.
- App categorization uses process names only.
- Employee visibility must follow existing `EmployeeVisibilityScope`.
- Dashboard endpoints require `monitoring:read`.
- Monitoring-disabled clients must not collect policy-disabled data; this slice does not weaken existing policy gates.

## Data Flow

```text
Tray App collectors
  -> Agent Service offline queue
  -> Backend ingest APIs
  -> Activity snapshots / app snapshots / device snapshots
  -> Daily summary aggregation
  -> Dashboard query + alert evaluation
  -> Manager dashboard API response
```

## Metrics

Daily summary must compute:

- `TotalActiveMinutes`
- `TotalIdleMinutes`
- `TotalMeetingMinutes`
- `ProductiveAppMinutes`
- `PersonalAppMinutes`
- `UnknownAppMinutes`
- `TopAppsJson`
- `FocusMinutes`
- `ActivityScore`
- `DataCoveragePercentage`

The MVP app category classifier is a deterministic process-name classifier:

- Productive examples: `code`, `devenv`, `excel`, `winword`, `powerpnt`, `outlook`, `notepad`, `postman`, `ssms`.
- Meeting examples: `teams`, `zoom`, `slack`, `meet`, `webex`.
- Personal examples: `youtube`, `netflix`, `spotify`, `steam`, `discord`, `facebook`, `instagram`, `tiktok`.
- Unknown examples: empty process names or names not matched above.

Meeting minutes count as meeting minutes and are not double-counted as productive app minutes unless a future persisted category table says otherwise.

## Dashboard API

Add a tenant monitoring endpoint:

```text
GET /api/v1/monitoring/dashboard?date=YYYY-MM-DD&page=1&pageSize=25&search=&departmentId=&legalEntityId=
```

Response must include:

- Total employees in the current visible page result.
- Active, idle, offline, and attention-needed counts for returned items.
- Average activity score across returned items with summaries.
- Employee cards containing name, department, position, status, last captured time, active/idle minutes, activity score, top apps, and alert count.

Status rules:

- `active`: latest device state captured within 5 minutes and `IsIdle == false`.
- `idle`: latest device state captured within 5 minutes and `IsIdle == true`.
- `offline`: no latest device state or latest capture older than 5 minutes.

## Alerts

Pure alert evaluation must return non-persisted dashboard alerts:

- `late_login`: first clock-in is after configured shift start plus grace minutes.
- `early_logout`: last clock-out is before configured shift end minus grace minutes.
- `long_idle`: idle minutes exceed configured threshold.
- `low_activity_score`: activity score is below configured threshold when data coverage is meaningful.
- `low_data_coverage`: coverage is below configured threshold.

Default policy values:

- Shift start: `09:00`
- Shift end: `18:00`
- Grace minutes: `10`
- Long idle threshold: `120` minutes
- Low activity score threshold: `50`
- Low data coverage threshold: `60`

## Testing

Focused unit tests cover:

- App classification and summary category metrics.
- Top apps JSON generation.
- Dashboard status rollup.
- Alert rule evaluation.
- Query handler visibility and validation behavior where practical with existing fakes.

Focused verification command:

```powershell
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter Monitoring
```

