<#
.SYNOPSIS
    Regenerates the InitialSqlServer migration from the current EF model.

.DESCRIPTION
    Run this after merging the latest development branch, so model changes made on PostgreSQL land
    in the baseline. EF stamps a new migration with the current time, which would sort it after
    the hand-written migrations that depend on it, so the files are renamed back to the fixed
    baseline id. The hand-written migrations are left alone.

    Afterwards, review the diff and run the clean database tests:

        dotnet test tests/ONEVO.Tests.Integration/ONEVO.Tests.Integration.csproj -c Release --filter "FullyQualifiedName~SqlServerMigration"

    New tenant-owned tables also need an entry in RlsTableRegistry; the architecture tests fail
    until they have one.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$baselineId = '20261107081808_InitialSqlServer'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$migrations = Join-Path $root 'src\ONEVO.Infrastructure\Migrations'

Push-Location $root
try {
    Remove-Item (Join-Path $migrations '*_InitialSqlServer*.cs')
    Remove-Item (Join-Path $migrations 'ApplicationDbContextModelSnapshot.cs')

    # The model builds without a database, but the design-time factory needs a connection string.
    $env:ConnectionStrings__MigrationConnection = 'Server=localhost;Database=model_only;User ID=x;Password=x'
    dotnet ef migrations add InitialSqlServer -p src/ONEVO.Infrastructure -s src/ONEVO.Api -o Migrations
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef migrations add failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem $migrations -Filter '*_InitialSqlServer*.cs' | ForEach-Object {
        $generatedId = $_.Name -replace '(\.Designer)?\.cs$', ''
        $text = [IO.File]::ReadAllText($_.FullName).Replace($generatedId, $baselineId)
        [IO.File]::WriteAllText($_.FullName, $text)
        Rename-Item $_.FullName ($_.Name.Replace($generatedId, $baselineId))
    }

    Write-Host "Regenerated $baselineId. Review the diff, then run the SqlServerMigration tests."
}
finally {
    Remove-Item Env:\ConnectionStrings__MigrationConnection -ErrorAction SilentlyContinue
    Pop-Location
}
