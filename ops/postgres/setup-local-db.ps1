[CmdletBinding()]
param(
    [switch]$RunMigrations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-DotEnvFile {
    param([Parameter(Mandatory)][string]$Path)

    $values = @{}
    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) {
            continue
        }

        $separatorIndex = $line.IndexOf('=')
        if ($separatorIndex -le 0) {
            throw "Invalid .env line. Expected KEY=VALUE syntax."
        }

        $key = $line.Substring(0, $separatorIndex).Trim()
        $value = $line.Substring($separatorIndex + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $values[$key] = $value
    }

    return $values
}

function Assert-RequiredValue {
    param(
        [Parameter(Mandatory)][hashtable]$Values,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not $Values.ContainsKey($Name) -or [string]::IsNullOrWhiteSpace($Values[$Name])) {
        throw "Required local database setting '$Name' is missing from .env."
    }

    $value = $Values[$Name].Trim()
    if ($value -match '^<.*>$' -or
        $value -match '^(SET_VIA_ENV|CHANGE_ME|__SET_ME__|REPLACE_ME)$') {
        throw "Replace the placeholder value for '$Name' in .env before running local database setup."
    }
}

function Invoke-PsqlChecked {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$FailureMessage
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage psql exited with code $LASTEXITCODE."
    }
}

function Find-PsqlExecutable {
    $pathCommand = Get-Command psql -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand) {
        return $pathCommand.Source
    }

    $postgresInstallRoot = Join-Path $env:ProgramFiles 'PostgreSQL'
    if (Test-Path -LiteralPath $postgresInstallRoot -PathType Container) {
        $installedClient = Get-ChildItem -LiteralPath $postgresInstallRoot `
            -Filter 'psql.exe' -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object -Property FullName -Descending |
            Select-Object -First 1

        if ($null -ne $installedClient) {
            return $installedClient.FullName
        }
    }

    return $null
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$envPath = Join-Path $repoRoot '.env'
$bootstrapPath = Join-Path $PSScriptRoot 'local-bootstrap-roles.sql'
$postMigrationGrantsPath = Join-Path $PSScriptRoot 'local-post-migration-grants.sql'

# Fixed conventional NOLOGIN role name that owns the sole pre-tenant base-login lookup
# function. Not password-protected (NOLOGIN), so unlike app_user/migrator_user it does not
# need to come from .env; production/staging must provision the equivalent role and grants
# through their own deployment process, exactly as this file's header comment already
# requires for app_user/migrator_user.
$baseLoginFnOwner = 'onevo_auth_base_login_fn_owner'

if (-not (Test-Path -LiteralPath $envPath -PathType Leaf)) {
    throw "Local .env file was not found. Copy .env.example to .env, replace the local password placeholders, and run this command again."
}

$psqlExecutable = Find-PsqlExecutable
if ([string]::IsNullOrWhiteSpace($psqlExecutable)) {
    throw "psql was not found on PATH or in the standard PostgreSQL installation directory. Install the PostgreSQL client tools and run this command again."
}

$settings = Read-DotEnvFile -Path $envPath
$requiredNames = @(
    'ONEVO_DB_HOST',
    'ONEVO_DB_PORT',
    'ONEVO_DB_NAME',
    'ONEVO_DB_ADMIN_USER',
    'ONEVO_DB_ADMIN_PASSWORD',
    'ONEVO_DB_APP_USER',
    'ONEVO_DB_APP_PASSWORD',
    'ONEVO_DB_MIGRATOR_USER',
    'ONEVO_DB_MIGRATOR_PASSWORD'
)

foreach ($requiredName in $requiredNames) {
    Assert-RequiredValue -Values $settings -Name $requiredName
}

if ($settings['ONEVO_DB_APP_USER'] -cne 'onevo_app') {
    throw "ONEVO_DB_APP_USER must be the restricted onevo_app runtime role."
}

if ($settings['ONEVO_DB_MIGRATOR_USER'] -cne 'onevo_migrator') {
    throw "ONEVO_DB_MIGRATOR_USER must be the onevo_migrator schema role."
}

if ($settings.ContainsKey('ConnectionStrings__DefaultConnection') -or
    $settings.ContainsKey('ConnectionStrings__MigrationConnection')) {
    Write-Warning "ConnectionStrings__* entries in .env are ignored. Remove them; this helper builds both connection strings from the ONEVO_DB_* values."
}

$parsedPort = 0
if (-not [int]::TryParse($settings['ONEVO_DB_PORT'], [ref]$parsedPort) -or
    $parsedPort -lt 1 -or
    $parsedPort -gt 65535) {
    throw "ONEVO_DB_PORT must be a valid integer."
}

$hostName = $settings['ONEVO_DB_HOST']
$port = $settings['ONEVO_DB_PORT']
$databaseName = $settings['ONEVO_DB_NAME']
$adminUser = $settings['ONEVO_DB_ADMIN_USER']
$adminPassword = $settings['ONEVO_DB_ADMIN_PASSWORD']
$appUser = $settings['ONEVO_DB_APP_USER']
$appPassword = $settings['ONEVO_DB_APP_PASSWORD']
$migratorUser = $settings['ONEVO_DB_MIGRATOR_USER']
$migratorPassword = $settings['ONEVO_DB_MIGRATOR_PASSWORD']

$migrationConnection = "Host=$hostName;Port=$port;Database=$databaseName;Username=$migratorUser;Password=$migratorPassword"

$commonArguments = @(
    '--no-psqlrc',
    '--set=ON_ERROR_STOP=1',
    '--host', $hostName,
    '--port', $port,
    '--username', $adminUser
)

$createDatabaseSql = @"
SELECT format('CREATE DATABASE %I', :'db_name')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'db_name')
\gexec
"@

$previousPgPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
try {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $adminPassword, 'Process')

    $authenticationArguments = $commonArguments + @(
        '--dbname', 'postgres',
        '--command', 'SELECT 1;'
    )
    Invoke-PsqlChecked -Executable $psqlExecutable -Arguments $authenticationArguments `
        -FailureMessage 'PostgreSQL admin authentication failed. ONEVO_DB_ADMIN_PASSWORD in the repo-root .env may be wrong.'

    Write-Host "Ensuring local PostgreSQL database exists..."
    $createDatabaseSqlPath = Join-Path (
        [System.IO.Path]::GetTempPath()) (
        "onevo-create-database-{0}.sql" -f [Guid]::NewGuid().ToString('N'))
    try {
        [System.IO.File]::WriteAllText(
            $createDatabaseSqlPath,
            $createDatabaseSql,
            [System.Text.UTF8Encoding]::new($false))

        $createDatabaseArguments = $commonArguments + @(
            '--dbname', 'postgres',
            '--set', "db_name=$databaseName",
            '--file', $createDatabaseSqlPath
        )
        Invoke-PsqlChecked -Executable $psqlExecutable -Arguments $createDatabaseArguments `
            -FailureMessage 'Local database creation/check failed.'
    }
    finally {
        if (Test-Path -LiteralPath $createDatabaseSqlPath -PathType Leaf) {
            Remove-Item -LiteralPath $createDatabaseSqlPath -Force
        }
    }

    Write-Host "Applying restricted local runtime and migration roles..."
    $bootstrapArguments = $commonArguments + @(
        '--dbname', $databaseName,
        '--set', "db_name=$databaseName",
        '--set', "app_user=$appUser",
        '--set', "app_password=$appPassword",
        '--set', "migrator_user=$migratorUser",
        '--set', "migrator_password=$migratorPassword",
        '--set', "base_login_fn_owner=$baseLoginFnOwner",
        '--file', $bootstrapPath
    )
    Invoke-PsqlChecked -Executable $psqlExecutable -Arguments $bootstrapArguments `
        -FailureMessage 'Local PostgreSQL role bootstrap failed.'
}
finally {
    [Environment]::SetEnvironmentVariable('PGPASSWORD', $previousPgPassword, 'Process')
}

Write-Host "Local PostgreSQL database roles are configured successfully."
Write-Host "Database: $databaseName"
Write-Host "Host: $hostName"
Write-Host "Port: $port"
Write-Host "Runtime role: $appUser"
Write-Host "Migration role: $migratorUser"

if ($RunMigrations) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "dotnet was not found on PATH, so EF migrations could not run."
    }

    $previousMigrationConnection = [Environment]::GetEnvironmentVariable(
        'ConnectionStrings__MigrationConnection',
        'Process')
    try {
        [Environment]::SetEnvironmentVariable(
            'ConnectionStrings__MigrationConnection',
            $migrationConnection,
            'Process')

        Write-Host "Applying EF migrations with the onevo_migrator connection..."
        Push-Location $repoRoot
        try {
            & $dotnetCommand.Source ef database update `
                --project 'src\ONEVO.Infrastructure\ONEVO.Infrastructure.csproj' `
                --startup-project 'src\ONEVO.Api\ONEVO.Api.csproj'

            if ($LASTEXITCODE -ne 0) {
                throw "EF migration command exited with code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            'ConnectionStrings__MigrationConnection',
            $previousMigrationConnection,
            'Process')
    }

    Write-Host "EF migrations completed successfully."

    Write-Host "Applying post-migration object grants..."
    $previousPgPasswordForGrants = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('PGPASSWORD', $adminPassword, 'Process')

        $postMigrationGrantsArguments = $commonArguments + @(
            '--dbname', $databaseName,
            '--set', "base_login_fn_owner=$baseLoginFnOwner",
            '--file', $postMigrationGrantsPath
        )
        Invoke-PsqlChecked -Executable $psqlExecutable -Arguments $postMigrationGrantsArguments `
            -FailureMessage 'Local PostgreSQL post-migration object grants failed.'
    }
    finally {
        [Environment]::SetEnvironmentVariable('PGPASSWORD', $previousPgPasswordForGrants, 'Process')
    }

    Write-Host "Post-migration object grants applied successfully."
}
else {
    Write-Host "Skipping post-migration object grants: pass -RunMigrations to apply them, because public.users/public.tenants may not exist yet without migrations."
}
