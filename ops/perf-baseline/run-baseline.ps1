<#
Task 06 Part B: the endpoint timing baseline in one command.

Starts the API in Release against a THROWAWAY local database with the performance baseline seed on, waits for the
seed and startup jobs, times every tenant GET endpoint (baseline.py), writes the report and stops the API.

  ./ops/perf-baseline/run-baseline.ps1 -Database CrownusDb_perf

The database must already exist with migrations applied (ops/sqlserver/setup-local-db.ps1 -Database CrownusDb_perf
-RunMigrations). Never point this at a shared database: the seed adds about 475 people and 20,000 tasks.

Needs: hosts entries for -RootHost and -TenantHost (crownus.test and dapi.crownus.test by default), the backend certificate in .certs, Python 3, and
BASELINE_EMAIL / BASELINE_PASSWORD set to the seeded Dapi owner (a test account; see DevSmokeTestTenantSeeder).
#>
param(
    [Parameter(Mandatory = $true)][string]$Database,
    [int]$Port = 7339,
    [string]$RootHost = "crownus.test",
    [string]$TenantHost = "dapi.crownus.test",
    [int]$Runs = 20,
    [string]$EnvFile = "",
    [string]$CertDir = "",
    [string]$Only = "",
    [int]$SettleSeconds = 30,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $EnvFile) { $EnvFile = Join-Path $root ".env" }
if (-not $CertDir) { $CertDir = Join-Path $root ".certs" }
if ($Database -notmatch '(?i)perf|baseline') {
    throw "Use a throwaway database whose name says so (for example CrownusDb_perf), not $Database."
}
if (-not $env:BASELINE_EMAIL -or -not $env:BASELINE_PASSWORD) { throw "Set BASELINE_EMAIL and BASELINE_PASSWORD first." }

# The API reads the repo-root .env only; a worktree has none, so pass the values in as process variables.
$saved = @{}
function Set-ProcessVariable([string]$name, [string]$value) {
    if (-not $saved.ContainsKey($name)) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    [Environment]::SetEnvironmentVariable($name, $value)
}
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
        $key, $value = $_ -split '=', 2
        $key = $key.Trim()
        if (-not [Environment]::GetEnvironmentVariable($key)) { Set-ProcessVariable $key $value.Trim().Trim('"') }
    }
}

$log = Join-Path ([IO.Path]::GetTempPath()) "crownus-baseline-api-$Port.log"
Set-ProcessVariable "ASPNETCORE_ENVIRONMENT" "Development"
Set-ProcessVariable "ASPNETCORE_URLS" "https://localhost:$Port"
Set-ProcessVariable "CROWNUS_DB_NAME" $Database
Set-ProcessVariable "Kestrel__Certificates__Default__Path" (Join-Path $CertDir "backend-crownus-cert.pem")
Set-ProcessVariable "Kestrel__Certificates__Default__KeyPath" (Join-Path $CertDir "backend-crownus-key.pem")
Set-ProcessVariable "Seeding__PerformanceBaseline__Enabled" "true"
# One signed-in user makes every call; the per-user limit would measure the limiter, not the endpoint.
Set-ProcessVariable "RateLimiting__Enabled" "false"
# Production log levels: Development logs every SQL command, which slows the seed and adds to every timing.
Set-ProcessVariable "Serilog__MinimumLevel__Default" "Information"
Set-ProcessVariable "Serilog__MinimumLevel__Override__Microsoft" "Warning"
Set-ProcessVariable "Serilog__MinimumLevel__Override__Microsoft.Hosting.Lifetime" "Information"
Set-ProcessVariable "Serilog__MinimumLevel__Override__System" "Warning"
Set-ProcessVariable "Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore" "Warning"
Set-ProcessVariable "Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command" "Warning"

$api = $null
try {
    if (-not $NoBuild) {
        dotnet build (Join-Path $root "src/ONEVO.Api") -c Release -v q -nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    }

    Write-Host "Starting the API on port $Port against $Database (log: $log)"
    $api = Start-Process dotnet -PassThru -NoNewWindow -WorkingDirectory $root -RedirectStandardOutput $log `
        -RedirectStandardError "$log.err" `
        -ArgumentList "run", "--project", "src/ONEVO.Api", "-c", "Release", "--no-launch-profile", "--no-build"

    # The seed runs before the server starts listening; the first run takes several minutes.
    $deadline = (Get-Date).AddMinutes(40)
    while ($true) {
        Start-Sleep -Seconds 3
        $text = if (Test-Path $log) { Get-Content $log -Raw } else { "" }
        if ($text -match "Application started") { break }
        if ($api.HasExited -or $text -match "DapiPerformanceBaselineSeeder failed|terminated unexpectedly") {
            throw "The API did not start. See $log"
        }
        if ((Get-Date) -gt $deadline) { throw "The API did not start within 40 minutes. See $log" }
    }
    $seeded = Select-String -Path $log -Pattern "Performance baseline data seeded.*" | Select-Object -Last 1
    if ($seeded) { Write-Host $seeded.Matches[0].Value }
    Start-Sleep -Seconds $SettleSeconds

    $arguments = @((Join-Path $PSScriptRoot "baseline.py"), "--log", $log, "--runs", $Runs, "--insecure",
        "--root-url", "https://${RootHost}:$Port", "--tenant-url", "https://${TenantHost}:$Port",
        "--origin", "https://${TenantHost}:4200",
        "--openapi", (Join-Path $root "docs/api/openapi/tenant-v1.json"),
        "--out", (Join-Path $root "docs/reports/performance/2026-11-baseline.md"),
        "--json", (Join-Path $root "docs/reports/performance/2026-11-baseline.json"))
    if ($Only) { $arguments += @("--only", $Only) }
    python @arguments
    if ($LASTEXITCODE -ne 0) { throw "The timing run failed." }
}
finally {
    if ($api -and -not $api.HasExited) {
        # dotnet run starts the API as a child process; stop the whole tree.
        taskkill /PID $api.Id /T /F | Out-Null
    }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
