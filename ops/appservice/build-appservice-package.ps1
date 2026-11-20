<#
.SYNOPSIS
  Builds the Azure App Service zip package: API publish + web app + admin console.

.DESCRIPTION
  1. dotnet publish src/ONEVO.Api (Release) into a fresh folder under the system temp folder.
  2. ng build web and admin with configuration 'singlehost' (-Mode SingleHost) or 'production' (-Mode Subdomain).
  3. Copies web dist/.../browser to <pkg>/wwwroot/web and admin to <pkg>/wwwroot/admin-console.
  4. Verifies the admin index.html base href matches the mode (/admin-console/ vs /).
  5. Verifies the web index.html inline script hashes are present in SecurityHeadersMiddleware.SpaCsp.
     Writes -BuildId to wwwroot/web/build-id.txt so a deploy can wait for this exact build.
  6. Zips the package and prints its SHA-256.

  Never runs npm install/ci, ng serve, dotnet test or anything that touches a server.
  Works in Windows PowerShell 5.1 and pwsh.
#>
[CmdletBinding()]
param(
    [ValidateSet('SingleHost', 'Subdomain')]
    [string]$Mode = 'SingleHost',

    [string]$WebRepo,
    [string]$AdminRepo,

    # Folder the zip is written to.
    [string]$OutDir,

    # Output zip file name; default crownus-appservice-<yyyy-MM-dd>.zip
    [string]$ZipName = ("crownus-appservice-{0}.zip" -f (Get-Date -Format 'yyyy-MM-dd')),

    # Written to wwwroot/web/build-id.txt (served at /build-id.txt) so a deploy can wait for this exact build.
    [ValidatePattern('^[A-Za-z0-9._-]{1,100}$')]
    [string]$BuildId = ("local-{0}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
)

$ErrorActionPreference = 'Stop'

$BackendRepo = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
if (-not $OutDir) { $OutDir = Split-Path $BackendRepo -Parent }
$Parent = Split-Path $BackendRepo -Parent
$Suffix = if ((Split-Path $BackendRepo -Leaf) -like '*-singlehost') { '-singlehost' } else { '' }
if (-not $WebRepo)   { $WebRepo   = Join-Path $Parent "Hrms--Web-application---front-end---v1$Suffix" }
if (-not $AdminRepo) { $AdminRepo = Join-Path $Parent "HRMS-Platform-Administration-Front-End-v1$Suffix" }

$NgConfig = if ($Mode -eq 'SingleHost') { 'singlehost' } else { 'production' }
$ExpectedAdminBase = if ($Mode -eq 'SingleHost') { '/admin-console/' } else { '/' }

function Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Fail([string]$m) { throw $m }
function Assert-Exit([string]$what) { if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit code $LASTEXITCODE)" } }

foreach ($p in @($WebRepo, $AdminRepo)) {
    if (-not (Test-Path (Join-Path $p 'angular.json'))) { Fail "Not an Angular repo (no angular.json): $p" }
    if (-not (Test-Path (Join-Path $p 'node_modules'))) { Fail "node_modules missing in $p (this script never runs npm install)" }
}

# Output folder of an app, read from angular.json (outputPath if set, else dist/<project>/browser).
function Get-BrowserDist([string]$repo) {
    $json = Get-Content (Join-Path $repo 'angular.json') -Raw | ConvertFrom-Json
    $name = ($json.projects.PSObject.Properties | Select-Object -First 1).Name
    $opts = $json.projects.$name.architect.build.options
    $op = $opts.outputPath
    if ($op -is [string]) { $base = $op; $browser = 'browser' }
    elseif ($op -and $op.base) { $base = $op.base; $browser = if ($op.browser -ne $null) { $op.browser } else { 'browser' } }
    else { $base = "dist/$name"; $browser = 'browser' }
    $path = Join-Path $repo (Join-Path $base $browser)
    return $path
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$Work = Join-Path ([System.IO.Path]::GetTempPath()) "crownus-appservice-$stamp"
$Pkg = Join-Path $Work 'publish'
New-Item -ItemType Directory -Path $Pkg -Force | Out-Null

# 1. API
Step "dotnet publish -> $Pkg"
Push-Location $BackendRepo
try {
    & dotnet publish src/ONEVO.Api -c Release -o $Pkg
    Assert-Exit 'dotnet publish'
} finally { Pop-Location }

# 2. Angular builds
function Build-Ng([string]$repo, [string]$label) {
    $dist = Get-BrowserDist $repo
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }   # never ship a stale build
    Step "ng build $label (configuration $NgConfig)"
    Push-Location $repo
    try {
        $ngJs = Join-Path $repo 'node_modules/@angular/cli/bin/ng.js'
        if (-not (Test-Path $ngJs)) { Fail "Angular CLI not found at $ngJs" }
        & node $ngJs build --configuration $NgConfig | Out-Host   # Out-Host keeps build output out of the function's return value
        Assert-Exit "ng build ($label)"
    } finally { Pop-Location }
    if (-not (Test-Path (Join-Path $dist 'index.html'))) { Fail "$label build produced no index.html at $dist" }
    return $dist
}
$WebDist = Build-Ng $WebRepo 'web'
$AdminDist = Build-Ng $AdminRepo 'admin console'

# 3. Copy into wwwroot
$WebTarget = Join-Path $Pkg 'wwwroot/web'
$AdminTarget = Join-Path $Pkg 'wwwroot/admin-console'
New-Item -ItemType Directory -Path $WebTarget, $AdminTarget -Force | Out-Null
Copy-Item (Join-Path $WebDist '*') $WebTarget -Recurse -Force
Copy-Item (Join-Path $AdminDist '*') $AdminTarget -Recurse -Force
# No trailing newline, so the deploy health check can compare the body exactly.
[System.IO.File]::WriteAllText((Join-Path $WebTarget 'build-id.txt'), $BuildId)
Step "Build id: $BuildId"

# 4. Admin base href must match the mode
$adminHtml = Get-Content (Join-Path $AdminTarget 'index.html') -Raw
$m = [regex]::Match($adminHtml, '<base\s+href\s*=\s*["'']([^"'']*)["'']', 'IgnoreCase')
if (-not $m.Success) { Fail 'Admin index.html has no <base href>' }
$actualBase = $m.Groups[1].Value
if ($actualBase -ne $ExpectedAdminBase) {
    Fail "Admin base href is '$actualBase' but mode $Mode needs '$ExpectedAdminBase'"
}
Step "Admin base href OK: $actualBase"

# 5. Web index.html inline script hashes must be in SecurityHeadersMiddleware.SpaCsp
$mw = Get-Content (Join-Path $BackendRepo 'src/ONEVO.Api/Middleware/SecurityHeadersMiddleware.cs') -Raw
$cspHashes = [regex]::Matches($mw, "'sha256-([A-Za-z0-9+/=]+)'") | ForEach-Object { $_.Groups[1].Value }
if (-not $cspHashes) { Fail 'No sha256 hashes found in SecurityHeadersMiddleware.SpaCsp (parser out of date?)' }
function Get-Sha256B64([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))) }
    finally { $sha.Dispose() }
}
$webHtml = Get-Content (Join-Path $WebTarget 'index.html') -Raw
$inline = @()
foreach ($sm in [regex]::Matches($webHtml, '<script(?<attrs>[^>]*)>(?<body>.*?)</script>', 'Singleline,IgnoreCase')) {
    if ($sm.Groups['attrs'].Value -match '\bsrc\s*=') { continue }
    if ($sm.Groups['body'].Value.Trim().Length -eq 0) { continue }
    $inline += $sm.Groups['body'].Value
}
if ($inline.Count -eq 0) { Fail 'Web index.html has no inline script; SpaCsp hashes and the theme script are out of sync' }
foreach ($body in $inline) {
    $h = Get-Sha256B64 $body
    if ($cspHashes -notcontains $h) {
        Fail "Web inline script hash sha256-$h is NOT in SpaCsp. Update SecurityHeadersMiddleware.SpaCsp (script changed or line endings differ)."
    }
    Step "Web inline script hash present in SpaCsp: sha256-$h"
}
# Angular critical-CSS loader handler (needs 'unsafe-hashes')
foreach ($om in [regex]::Matches($webHtml, '\bonload\s*=\s*"([^"]*)"', 'IgnoreCase')) {
    $h = Get-Sha256B64 $om.Groups[1].Value
    if ($cspHashes -notcontains $h) { Fail "Web inline onload handler hash sha256-$h is NOT in SpaCsp" }
}
if ($webHtml -match '\bonload\s*=') { Step 'Web onload handler hash present in SpaCsp' }

# 6. Zip + SHA-256
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$Zip = Join-Path $OutDir $ZipName
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Step "Zipping -> $Zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Forward-slash entry names (Compress-Archive in 5.1 writes backslashes, which break Linux/Kudu extraction).
$zipStream = [System.IO.File]::Open($Zip, [System.IO.FileMode]::CreateNew)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $sep = [System.IO.Path]::DirectorySeparatorChar
        $root = (Resolve-Path $Pkg).Path.TrimEnd($sep) + $sep
        foreach ($f in Get-ChildItem $Pkg -Recurse -File) {
            $entry = $f.FullName.Substring($root.Length).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
} finally { $zipStream.Dispose() }

$hash = (Get-FileHash $Zip -Algorithm SHA256).Hash
$size = (Get-Item $Zip).Length
Write-Host ''
Write-Host "Mode:    $Mode (ng configuration $NgConfig)"
Write-Host "Package: $Zip"
Write-Host ("Size:    {0:N1} MB ({1} bytes)" -f ($size / 1MB), $size)
Write-Host "SHA-256: $hash"
Write-Host "Staging folder kept at $Work (safe to delete)."

# GitHub Actions: expose the zip to later steps and the run summary.
if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "zip_path=$Zip"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "zip_sha256=$hash"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "build_id=$BuildId"
}
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value @(
        '### App Service package',
        '',
        "- Mode: ``$Mode`` (ng configuration ``$NgConfig``)",
        ("- Zip: ``$ZipName`` ({0:N1} MB)" -f ($size / 1MB)),
        "- SHA-256: ``$hash``",
        "- Build id: ``$BuildId``",
        ''
    )
}
