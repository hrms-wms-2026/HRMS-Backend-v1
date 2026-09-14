[CmdletBinding()]
param(
    [string]$RootDomain = 'onexso.com',
    [string[]]$TenantSubdomains = @('acme', 'dapi', 'admin')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-MkcertExecutable {
    $pathCommand = Get-Command mkcert -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand) {
        return $pathCommand.Source
    }

    $wellKnownPath = 'C:\tools\mkcert\mkcert.exe'
    if (Test-Path -LiteralPath $wellKnownPath -PathType Leaf) {
        return $wellKnownPath
    }

    return $null
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$certsDir = Join-Path $repoRoot '.certs'
$certLabel = ($RootDomain -split '\.')[0]
$certPath = Join-Path $certsDir "backend-$certLabel-cert.pem"
$keyPath = Join-Path $certsDir "backend-$certLabel-key.pem"

$mkcertExecutable = Find-MkcertExecutable
if ([string]::IsNullOrWhiteSpace($mkcertExecutable)) {
    throw "mkcert was not found on PATH or at C:\tools\mkcert\mkcert.exe. Install it first: choco install mkcert (or download from https://github.com/FiloSottile/mkcert/releases) and re-run this script."
}

if ((Test-Path -LiteralPath $certPath -PathType Leaf) -and (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
    Write-Host "Certificate already exists at $certPath - skipping generation."
    Write-Host "Delete both .pem files under .certs\ and re-run this script to regenerate."
}
else {
    New-Item -ItemType Directory -Path $certsDir -Force | Out-Null

    Write-Host "Installing mkcert local root CA into the Windows/browser trust stores (idempotent, safe to re-run)..."
    & $mkcertExecutable '-install'
    if ($LASTEXITCODE -ne 0) {
        throw "mkcert -install failed with exit code $LASTEXITCODE."
    }

    $sanList = @($RootDomain, '127.0.0.1', '::1')
    foreach ($subdomain in $TenantSubdomains) {
        $sanList += "$subdomain.$RootDomain"
    }
    $sanList += "*.$RootDomain"

    Write-Host "Generating certificate for: $($sanList -join ', ')"
    & $mkcertExecutable '-key-file' $keyPath '-cert-file' $certPath @sanList
    if ($LASTEXITCODE -ne 0) {
        throw "mkcert certificate generation failed with exit code $LASTEXITCODE."
    }

    Write-Host "Certificate written to $certPath"
}

Write-Host ""
Write-Host "Confirm appsettings.Development.json points Kestrel:Certificates:Default at:"
Write-Host "  Path:    ../../.certs/backend-$certLabel-cert.pem"
Write-Host "  KeyPath: ../../.certs/backend-$certLabel-key.pem"

Write-Host ""
Write-Host "Add these entries to C:\Windows\System32\drivers\etc\hosts if not already present"
Write-Host "(requires an elevated editor - this script does not modify system files automatically):"
Write-Host "  127.0.0.1 $RootDomain"
foreach ($subdomain in $TenantSubdomains) {
    Write-Host "  127.0.0.1 $subdomain.$RootDomain"
}

$hostsPath = "$env:WINDIR\System32\drivers\etc\hosts"
if (Test-Path -LiteralPath $hostsPath -PathType Leaf) {
    $hostsContent = Get-Content -LiteralPath $hostsPath -Raw
    $missing = @()
    if ($hostsContent -notmatch [regex]::Escape($RootDomain)) {
        $missing += $RootDomain
    }
    foreach ($subdomain in $TenantSubdomains) {
        if ($hostsContent -notmatch [regex]::Escape("$subdomain.$RootDomain")) {
            $missing += "$subdomain.$RootDomain"
        }
    }
    if ($missing.Count -eq 0) {
        Write-Host ""
        Write-Host "All required hosts entries already present."
    }
    else {
        Write-Host ""
        Write-Warning "Missing hosts entries: $($missing -join ', ')"
    }
}

Write-Host ""
Write-Host "Local certificate setup complete."
