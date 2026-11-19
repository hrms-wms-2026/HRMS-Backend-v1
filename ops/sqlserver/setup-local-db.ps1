<#
Prepares a local SQL Server database for development.

Needs a privileged login on a LOCAL server. Never point this at a shared server: applying
database changes there is a separate, agreed deployment step.

  ./ops/sqlserver/setup-local-db.ps1 -Server "localhost,1433" -RunMigrations

Passwords come from CROWNUS_DB_APP_PASSWORD and CROWNUS_DB_MIGRATOR_PASSWORD in the repo-root .env.
#>
param(
    [string]$Server = "localhost,1433",
    [string]$AdminUser = "sa",
    [string]$Database = "CrownusDb",
    [switch]$RunMigrations
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$values = @{}
$envFile = Join-Path $root ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
        $key, $value = $_ -split '=', 2
        $values[$key.Trim()] = $value.Trim().Trim('"')
    }
}

$appPassword = $values["CROWNUS_DB_APP_PASSWORD"]
$migratorPassword = $values["CROWNUS_DB_MIGRATOR_PASSWORD"]
if (-not $appPassword -or -not $migratorPassword) {
    throw "Set CROWNUS_DB_APP_PASSWORD and CROWNUS_DB_MIGRATOR_PASSWORD in .env first."
}

$adminPassword = Read-Host "Password for $AdminUser" -AsSecureString
$env:SQLCMDPASSWORD = [System.Net.NetworkCredential]::new("", $adminPassword).Password

function Invoke-Script([string]$file) {
    sqlcmd -S $Server -U $AdminUser -C -b -i (Join-Path $PSScriptRoot $file) `
        -v "DB_NAME=$Database" "APP_PASSWORD=$appPassword" "MIGRATOR_PASSWORD=$migratorPassword"
    if ($LASTEXITCODE -ne 0) { throw "$file failed." }
}

try {
    Invoke-Script "bootstrap-logins.sql"
    if ($RunMigrations) {
        $builder = "Server=$Server;Database=$Database;User ID=crownus_migrator;Password=$migratorPassword;TrustServerCertificate=True"
        $env:ConnectionStrings__MigrationConnection = $builder
        dotnet ef database update -p (Join-Path $root "src/ONEVO.Infrastructure") -s (Join-Path $root "src/ONEVO.Api")
        if ($LASTEXITCODE -ne 0) { throw "Migrations failed." }
        Invoke-Script "post-migration-grants.sql"
    }
}
finally {
    Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:ConnectionStrings__MigrationConnection -ErrorAction SilentlyContinue
}
