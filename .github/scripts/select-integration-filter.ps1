<#
.SYNOPSIS
    Chooses which ONEVO.Tests.Integration tests a PR needs to run, based on changed file paths.

.DESCRIPTION
    Full Testcontainers-backed integration runs are slow, so PRs should only run the focused
    subset relevant to what actually changed. This script maps changed file paths to one or
    more `dotnet test --filter` expressions (via FullyQualifiedName substring matches against
    real namespaces/class names in tests/ONEVO.Tests.Integration), combines them with `|` into
    a single filter, and falls back to the full suite whenever it cannot confidently narrow the
    scope. It never invents a filter that could under-test a change - "skip" and "full
    integration" are the two safe extremes; a combined filter is only used when the routing is
    grounded in an explicit mapping rule below.

    This script does not run `dotnet test` itself - it only decides what should run. The
    calling workflow step runs the actual command using the values this script outputs.

.PARAMETER ChangedFiles
    Changed file paths as a string array. Use this for direct/programmatic calls (including the
    -SelfTest examples below).

.PARAMETER ChangedFilesPath
    Path to a text file with one changed file path per line (e.g. the output of
    `git diff --name-only <merge-base>...<head>`). Use this from the CI workflow step.

.PARAMETER SelfTest
    Runs the built-in sanity examples in this file (see the EXAMPLES section) and exits with a
    non-zero code if any of them fail. No git/CI context is required.

.OUTPUTS
    A PSCustomObject with:
      Skip            [bool]   true when no integration run is needed at all.
      FullIntegration [bool]   true when the full ONEVO.Tests.Integration suite should run.
      Filter          [string] the combined --filter expression (empty when Skip or
                                FullIntegration is true).
      Reasons         [string[]] human-readable reasons for the decision, one per rule that
                                fired, in the order they were evaluated.
      MatchedAreas    [string[]] names of the mapping-table areas that matched (for logging).

    When run outside -SelfTest, if $env:GITHUB_OUTPUT is set (i.e. running as a GitHub Actions
    step), this script also appends `skip`, `full_integration`, `filter`, and `reason` to it,
    and appends a human-readable summary table to $env:GITHUB_STEP_SUMMARY when that is set.

.EXAMPLE
    # Department-only change -> focused Department filter, not full integration.
    .\select-integration-filter.ps1 -ChangedFiles @('src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs')
    # -> FullIntegration=$false, Filter='FullyQualifiedName~Department'

.EXAMPLE
    # Two areas touched in one PR -> filters combined with a single '|', not two test runs.
    .\select-integration-filter.ps1 -ChangedFiles @(
        'src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment.cs',
        'src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition.cs'
    )
    # -> Filter='FullyQualifiedName~Department|FullyQualifiedName~Position'

.EXAMPLE
    # Docs/report/Postman-only change -> skip integration entirely.
    .\select-integration-filter.ps1 -ChangedFiles @('EMPLOYEE_ONBOARDING_APPROVE_SEND_INVITE_REPORT.md', 'ONEVO-HRMS.postman_collection.json')
    # -> Skip=$true

.EXAMPLE
    # Migration with no other mapped area touched -> "uncertain" -> full integration.
    .\select-integration-filter.ps1 -ChangedFiles @('src/ONEVO.Infrastructure/Migrations/20260811000000_AddSomething.cs')
    # -> FullIntegration=$true (reason names the migration as the trigger)

.EXAMPLE
    # Migration alongside a mapped feature change -> conservative migration filter is still
    # included, combined with the matched area - not escalated to full, because there is
    # corroborating evidence of the migration's scope. Matched-area filters are appended before
    # the migration filter, so the concatenation order is 'Department' then
    # 'ApiBoot|Migration|DbContext' - order is irrelevant to `dotnet test --filter` (it's an OR),
    # so callers should not depend on it.
    .\select-integration-filter.ps1 -ChangedFiles @(
        'src/ONEVO.Infrastructure/Migrations/20260811000000_AddDepartmentColumn.cs',
        'src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs'
    )
    # -> Filter='FullyQualifiedName~Department|FullyQualifiedName~ApiBoot|FullyQualifiedName~Migration|FullyQualifiedName~DbContext'

.EXAMPLE
    # src/ path with no mapped area at all -> "not confident" -> full integration.
    .\select-integration-filter.ps1 -ChangedFiles @('src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject.cs')
    # -> FullIntegration=$true

.EXAMPLE
    # Core HR / Employee / Onboarding change (Phase 1, actively developed) -> focused CoreHr
    # filter, not full integration.
    .\select-integration-filter.ps1 -ChangedFiles @('src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListOnboardingAccessGrantRequests/ListOnboardingAccessGrantRequestsQueryHandler.cs')
    # -> FullIntegration=$false, Filter='FullyQualifiedName~CoreHr|FullyQualifiedName~Employee|FullyQualifiedName~Onboarding'

.EXAMPLE
    # Only ONEVO.Tests.Unit/ONEVO.Tests.Architecture files changed -> skip integration entirely;
    # build-and-test's unit/architecture runs already cover them and neither suite touches
    # Testcontainers/Postgres.
    .\select-integration-filter.ps1 -ChangedFiles @('tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs')
    # -> Skip=$true

.EXAMPLE
    # An actual ONEVO.Tests.Integration test file changes (not Unit/Architecture) -> routes to
    # its area's filter like any other change under that area, it is not skipped.
    .\select-integration-filter.ps1 -ChangedFiles @('tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs')
    # -> FullIntegration=$false, Filter='FullyQualifiedName~Department'

.EXAMPLE
    # Run every example above as a pass/fail sanity check, no git/CI context needed:
    .\select-integration-filter.ps1 -SelfTest
#>
[CmdletBinding(DefaultParameterSetName = 'Files')]
param(
    [Parameter(ParameterSetName = 'Files')]
    [string[]] $ChangedFiles,

    [Parameter(ParameterSetName = 'FilesPath')]
    [string] $ChangedFilesPath,

    [Parameter(ParameterSetName = 'SelfTest')]
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Mapping table. Each area lists:
#   PathPatterns - wildcard patterns (PowerShell -like, so '*' matches any run of
#                  characters, including '/') matched against the full repo-relative,
#                  forward-slash-normalized path.
#   Keywords     - case-insensitive substrings matched against the same path (covers the
#                  task's "files containing X" rules, which are not tied to one folder).
#   Filter       - the FullyQualifiedName filter expression to use when this area matches.
#
# Path patterns and keywords were verified against the real namespaces/classes in
# tests/ONEVO.Tests.Integration (see CI_FOCUSED_INTEGRATION_TEST_ROUTING_REPORT.md) rather
# than guessed - e.g. FullyQualifiedName~LegalEntit matches the real
# ONEVO.Tests.Integration.OrgStructure.LegalEntity.LegalEntitiesIntegrationTests.
# ---------------------------------------------------------------------------
function New-IntegrationTestPathPatterns {
    <#
        Builds both the "area folder sits directly under the Integration project root" form
        (tests/ONEVO.Tests.Integration/<Name>/*) and the "area folder is nested one level deeper"
        form (tests/ONEVO.Tests.Integration/*/<Name>/*), for every area name given.

        This exists because the real folder depth is inconsistent across areas and gets it wrong
        either way if hard-coded once: Auth, DevPlatform, Monitoring, Storage, and CoreHr all sit
        directly under tests/ONEVO.Tests.Integration/ (e.g. tests/ONEVO.Tests.Integration/Auth/*),
        while LegalEntity/Department/Position sit one level deeper, under OrgStructure/ (e.g.
        tests/ONEVO.Tests.Integration/OrgStructure/Department/*). A single '*/<Name>/*' pattern
        silently misses the direct-child case (PowerShell -like's '*' still requires the literal
        '/' on both sides of <Name> to be present in the string, and a direct child has no
        leading '/' before <Name> once the fixed 'tests/ONEVO.Tests.Integration/' prefix is
        consumed) - this generalized helper produces both forms for every area so this class of
        bug can't recur if the real layout shifts again later.
    #>
    param([string[]] $Names)

    $patterns = @()
    foreach ($name in $Names) {
        $patterns += "tests/ONEVO.Tests.Integration/$name/*"
        $patterns += "tests/ONEVO.Tests.Integration/*/$name/*"
    }
    return $patterns
}

$script:Areas = @(
    @{
        Name         = 'Auth'
        PathPatterns = @(
            'src/ONEVO.Api/Controllers/*/Auth/*'
            'src/ONEVO.Api/Controllers/*/Legal/*'
            'src/ONEVO.Application/Features/Auth/*'
            'src/ONEVO.Application/Features/Legal/*'
            'src/ONEVO.Infrastructure/*/Auth/*'
        ) + (New-IntegrationTestPathPatterns -Names @('Auth', 'Legal'))
        Keywords     = @('Session', 'Csrf', 'Ticket', 'PasswordReset', 'Mfa', 'LegalAcceptance')
        Filter       = 'FullyQualifiedName~Auth|FullyQualifiedName~Legal|FullyQualifiedName~Password|FullyQualifiedName~Mfa|FullyQualifiedName~Session'
    }
    @{
        Name         = 'DevPlatform'
        PathPatterns = @(
            'src/ONEVO.Api/Controllers/Admin/*'
            'src/ONEVO.Application/Features/DevPlatform/*'
            'src/ONEVO.Infrastructure/Services/DevPlatform/*'
            'src/ONEVO.Infrastructure/Persistence/Seeders/*'
        ) + (New-IntegrationTestPathPatterns -Names @('DevPlatform', 'Admin'))
        Keywords     = @()
        Filter       = 'FullyQualifiedName~DevPlatform|FullyQualifiedName~Admin|FullyQualifiedName~TenantProvisioning|FullyQualifiedName~ApiBoot'
    }
    @{
        Name         = 'LegalEntity'
        PathPatterns = @(
            'src/*/LegalEntity/*'
            'src/*/LegalEntities/*'
            'src/ONEVO.Api/Controllers/Tenant/OrgStructure/LegalEntitiesController.cs'
        ) + (New-IntegrationTestPathPatterns -Names @('LegalEntity', 'LegalEntities'))
        Keywords     = @()
        Filter       = 'FullyQualifiedName~LegalEntit'
    }
    @{
        Name         = 'Department'
        PathPatterns = @(
            'src/*/Department/*'
            'src/*/Departments/*'
            'src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs'
        ) + (New-IntegrationTestPathPatterns -Names @('Department', 'Departments'))
        Keywords     = @()
        Filter       = 'FullyQualifiedName~Department'
    }
    @{
        Name         = 'Position'
        PathPatterns = @(
            'src/*/Position/*'
            'src/*/Positions/*'
            'src/ONEVO.Api/Controllers/Tenant/OrgStructure/PositionsController.cs'
        ) + (New-IntegrationTestPathPatterns -Names @('Position', 'Positions'))
        Keywords     = @('ManagementCoverage')
        Filter       = 'FullyQualifiedName~Position'
    }
    @{
        Name         = 'Monitoring'
        PathPatterns = @(
            'src/*/Monitoring/*'
            'src/*/Tray/*'
        ) + (New-IntegrationTestPathPatterns -Names @('Monitoring', 'Tray'))
        Keywords     = @('TrayActivation', 'TrayDevice', 'EmployeeCheckIn', 'MonitoringFaceScan')
        Filter       = 'FullyQualifiedName~Monitoring|FullyQualifiedName~Tray|FullyQualifiedName~CheckIn'
    }
    @{
        Name         = 'Storage'
        PathPatterns = @(
            'src/*/Storage/*'
            'src/*/File/*'
        ) + (New-IntegrationTestPathPatterns -Names @('Storage', 'File'))
        Keywords     = @('FileStorage', 'FileRecord', 'UploadReservation')
        Filter       = 'FullyQualifiedName~Storage|FullyQualifiedName~File'
    }
    @{
        # Core HR / Employee / Onboarding - Phase 1, under active development, so an unmapped
        # change here previously fell to the expensive full-integration fallback. Added per
        # explicit correction: paths/filter as specified, not narrowed or reinterpreted.
        Name         = 'CoreHr'
        PathPatterns = @(
            'src/*/CoreHr/*'
            'src/*/Employee/*'
            'src/*/Employees/*'
            'src/*/Onboarding/*'
        ) + (New-IntegrationTestPathPatterns -Names @('CoreHr', 'Employee', 'Employees', 'Onboarding'))
        Keywords     = @()
        Filter       = 'FullyQualifiedName~CoreHr|FullyQualifiedName~Employee|FullyQualifiedName~Onboarding'
    }
    @{
        Name         = 'Leave'
        PathPatterns = @(
            'src/*/Leave/*'
            'src/ONEVO.Api/Controllers/Tenant/Leave/*'
        ) + (New-IntegrationTestPathPatterns -Names @('Leave'))
        Keywords     = @('LeavePolicy', 'LeaveType', 'LeaveEntitlement', 'LeaveRequest')
        Filter       = 'FullyQualifiedName~Leave'
    }
)

$script:MigrationPathPattern  = 'src/ONEVO.Infrastructure/Migrations/*'
$script:MigrationFilter       = 'FullyQualifiedName~ApiBoot|FullyQualifiedName~Migration|FullyQualifiedName~DbContext'

function Get-NormalizedPaths {
    param([string[]] $Paths)

    $result = @()
    foreach ($p in $Paths) {
        if ($null -eq $p) { continue }
        $trimmed = $p.Trim()
        if ($trimmed.Length -eq 0) { continue }
        $result += $trimmed.Replace('\', '/')
    }
    return $result
}

function Test-PathMatchesArea {
    param(
        [string]   $Path,
        [hashtable] $Area
    )

    foreach ($pattern in $Area.PathPatterns) {
        if ($Path -like $pattern) { return $true }
    }
    foreach ($keyword in $Area.Keywords) {
        if ($Path -like "*$keyword*") { return $true }
    }
    return $false
}

function Get-IntegrationDecision {
    <#
        Core decision function. Takes normalized, forward-slash changed file paths and returns
        the PSCustomObject documented in the script's .OUTPUTS section above.
    #>
    param([string[]] $Files)

    $reasons      = New-Object System.Collections.Generic.List[string]
    $matchedAreas = New-Object System.Collections.Generic.List[string]

    if ($Files.Count -eq 0) {
        $reasons.Add('No changed files were provided; nothing to route.')
        return [PSCustomObject]@{
            Skip            = $true
            FullIntegration = $false
            Filter          = ''
            Reasons         = $reasons.ToArray()
            MatchedAreas    = $matchedAreas.ToArray()
        }
    }

    # Skip rule: nothing under src/ or tests/ changed (docs, *_REPORT.md, postman, .github,
    # ops, README, etc. - anything that cannot affect runtime behavior the integration suite
    # exercises).
    $backendRelevant = $Files | Where-Object { $_ -like 'src/*' -or $_ -like 'tests/*' }
    if (-not $backendRelevant -or $backendRelevant.Count -eq 0) {
        $reasons.Add('No changed file is under src/ or tests/ (docs/report/Postman/config-only change) - integration skipped.')
        return [PSCustomObject]@{
            Skip            = $true
            FullIntegration = $false
            Filter          = ''
            Reasons         = $reasons.ToArray()
            MatchedAreas    = $matchedAreas.ToArray()
        }
    }

    # Skip rule: only ONEVO.Tests.Unit and/or ONEVO.Tests.Architecture files changed - no src/
    # and no ONEVO.Tests.Integration file. Those two suites don't touch Testcontainers/Postgres
    # at all, and the always-run build-and-test job already covers them, so running (focused or
    # full) integration on top would be pure waste.
    $srcOrIntegrationFiles = $Files | Where-Object { $_ -like 'src/*' -or $_ -like 'tests/ONEVO.Tests.Integration/*' }
    if (-not $srcOrIntegrationFiles -or $srcOrIntegrationFiles.Count -eq 0) {
        $unitOrArchFiles = $Files | Where-Object { $_ -like 'tests/ONEVO.Tests.Unit/*' -or $_ -like 'tests/ONEVO.Tests.Architecture/*' }
        if ($unitOrArchFiles -and $unitOrArchFiles.Count -gt 0) {
            $reasons.Add("Only ONEVO.Tests.Unit/ONEVO.Tests.Architecture file(s) changed ($([string]::Join(', ', $unitOrArchFiles))) - already covered by build-and-test, integration skipped.")
            return [PSCustomObject]@{
                Skip            = $true
                FullIntegration = $false
                Filter          = ''
                Reasons         = $reasons.ToArray()
                MatchedAreas    = $matchedAreas.ToArray()
            }
        }
    }

    # Mapping-table areas.
    $matchedFilters = New-Object System.Collections.Generic.List[string]
    foreach ($area in $script:Areas) {
        $matches = $Files | Where-Object { Test-PathMatchesArea -Path $_ -Area $area }
        if ($matches -and $matches.Count -gt 0) {
            $matchedAreas.Add($area.Name)
            if (-not $matchedFilters.Contains($area.Filter)) {
                $matchedFilters.Add($area.Filter)
            }
            $reasons.Add("Matched area '$($area.Name)' via: $([string]::Join(', ', $matches))")
        }
    }

    # Migrations: always include the conservative ApiBoot/Migration/DbContext filter. Escalate
    # to full integration only when the migration is the *sole* signal - i.e. no other mapped
    # area corroborates its scope, so which tables/features it touches is genuinely uncertain.
    $migrationFiles = $Files | Where-Object { $_ -like $script:MigrationPathPattern }
    $migrationChanged = $migrationFiles -and $migrationFiles.Count -gt 0
    if ($migrationChanged) {
        if ($matchedAreas.Count -eq 0) {
            $reasons.Add("Migration file(s) changed with no corroborating mapped area ($([string]::Join(', ', $migrationFiles))) - scope is uncertain, running full integration.")
            return [PSCustomObject]@{
                Skip            = $false
                FullIntegration = $true
                Filter          = ''
                Reasons         = $reasons.ToArray()
                MatchedAreas    = $matchedAreas.ToArray()
            }
        }

        if (-not $matchedFilters.Contains($script:MigrationFilter)) {
            $matchedFilters.Add($script:MigrationFilter)
        }
        $reasons.Add("Migration file(s) changed alongside a mapped area - including conservative migration filter: $([string]::Join(', ', $migrationFiles))")
    }

    if ($matchedFilters.Count -gt 0) {
        return [PSCustomObject]@{
            Skip            = $false
            FullIntegration = $false
            Filter          = [string]::Join('|', $matchedFilters)
            Reasons         = $reasons.ToArray()
            MatchedAreas    = $matchedAreas.ToArray()
        }
    }

    # Backend/test source changed but nothing matched any mapping rule - safe fallback.
    $reasons.Add("Backend/test file(s) changed but did not map to any known focused area ($([string]::Join(', ', $backendRelevant))) - running full integration as a safe fallback.")
    return [PSCustomObject]@{
        Skip            = $false
        FullIntegration = $true
        Filter          = ''
        Reasons         = $reasons.ToArray()
        MatchedAreas    = $matchedAreas.ToArray()
    }
}

function Write-DecisionOutputs {
    param([PSCustomObject] $Decision)

    $reasonLine = [string]::Join(' | ', $Decision.Reasons)

    Write-Host '--- Integration routing decision ---'
    Write-Host "Skip:             $($Decision.Skip)"
    Write-Host "FullIntegration:  $($Decision.FullIntegration)"
    Write-Host "Filter:           $($Decision.Filter)"
    Write-Host "MatchedAreas:     $([string]::Join(', ', $Decision.MatchedAreas))"
    Write-Host 'Reasons:'
    foreach ($r in $Decision.Reasons) { Write-Host "  - $r" }

    if ($env:GITHUB_OUTPUT) {
        Add-Content -Path $env:GITHUB_OUTPUT -Value "skip=$($Decision.Skip.ToString().ToLower())"
        Add-Content -Path $env:GITHUB_OUTPUT -Value "full_integration=$($Decision.FullIntegration.ToString().ToLower())"
        Add-Content -Path $env:GITHUB_OUTPUT -Value "filter=$($Decision.Filter)"
        Add-Content -Path $env:GITHUB_OUTPUT -Value "reason=$reasonLine"
    }

    if ($env:GITHUB_STEP_SUMMARY) {
        $mode = 'focused'
        if ($Decision.Skip) { $mode = 'skipped' }
        elseif ($Decision.FullIntegration) { $mode = 'full' }

        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value '### Integration test routing'
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "- **Mode:** $mode"
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "- **Filter:** ``$($Decision.Filter)``"
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "- **Matched areas:** $([string]::Join(', ', $Decision.MatchedAreas))"
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value '- **Reasons:**'
        foreach ($r in $Decision.Reasons) {
            Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "  - $r"
        }
    }
}

function Invoke-SelfTest {
    $script:failures = 0

    function Assert-Decision {
        param([string] $Name, [string[]] $Files, [scriptblock] $Check)

        $normalized = Get-NormalizedPaths -Paths $Files
        $decision   = Get-IntegrationDecision -Files $normalized
        $ok         = & $Check $decision

        if ($ok) {
            Write-Host "PASS: $Name"
        }
        else {
            Write-Host "FAIL: $Name"
            Write-Host "  Skip=$($decision.Skip) FullIntegration=$($decision.FullIntegration) Filter=$($decision.Filter)"
            $script:failures = $script:failures + 1
        }
    }

    Assert-Decision -Name 'Department-only change routes to Department filter' `
        -Files @('src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Department' }

    Assert-Decision -Name 'Position management-coverage keyword routes to Position filter' `
        -Files @('src/ONEVO.Application/Features/OrgStructure/ManagementCoverageService.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Position' }

    Assert-Decision -Name 'Auth + Legal changes combine into one OR-joined filter' `
        -Files @(
            'src/ONEVO.Application/Features/Auth/Login/LoginCommandHandler.cs'
            'src/ONEVO.Application/Features/Legal/AcceptLegalDocumentCommand.cs'
        ) `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Auth|FullyQualifiedName~Legal|FullyQualifiedName~Password|FullyQualifiedName~Mfa|FullyQualifiedName~Session' }

    Assert-Decision -Name 'Two areas in one PR combine with a single pipe, no duplicate filters' `
        -Files @(
            'src/ONEVO.Application/Features/OrgStructure/Department/Commands/CreateDepartment.cs'
            'src/ONEVO.Application/Features/OrgStructure/Position/Commands/CreatePosition.cs'
        ) `
        -Check { param($d) $d.Filter -eq 'FullyQualifiedName~Department|FullyQualifiedName~Position' }

    Assert-Decision -Name 'Docs/report/Postman-only change skips integration' `
        -Files @('EMPLOYEE_ONBOARDING_APPROVE_SEND_INVITE_REPORT.md', 'ONEVO-HRMS.postman_collection.json') `
        -Check { param($d) $d.Skip -eq $true }

    Assert-Decision -Name 'Migration alone (no corroborating area) escalates to full integration' `
        -Files @('src/ONEVO.Infrastructure/Migrations/20260811000000_AddSomething.cs') `
        -Check { param($d) -not $d.Skip -and $d.FullIntegration -eq $true }

    Assert-Decision -Name 'Migration + mapped feature change stays focused, includes migration filter' `
        -Files @(
            'src/ONEVO.Infrastructure/Migrations/20260811000000_AddDepartmentColumn.cs'
            'src/ONEVO.Api/Controllers/Tenant/OrgStructure/DepartmentsController.cs'
        ) `
        -Check {
            param($d)
            # Order doesn't matter for an OR-joined --filter expression - check both parts are
            # present exactly once each, rather than asserting a fixed concatenation order.
            $parts = $d.Filter -split '\|FullyQualifiedName~' | ForEach-Object { $_ -replace '^FullyQualifiedName~', '' }
            (-not $d.FullIntegration) -and
            ($parts -contains 'Department') -and
            ($parts -contains 'ApiBoot') -and
            ($parts -contains 'Migration') -and
            ($parts -contains 'DbContext') -and
            ($parts.Count -eq 4)
        }

    Assert-Decision -Name 'Unmapped src/ path with no matching area falls back to full integration' `
        -Files @('src/ONEVO.Application/Features/WorkManagement/Projects/Commands/CreateProject.cs') `
        -Check { param($d) -not $d.Skip -and $d.FullIntegration -eq $true }

    Assert-Decision -Name 'No changed files at all is treated as skip' `
        -Files @() `
        -Check { param($d) $d.Skip -eq $true }

    Assert-Decision -Name 'CoreHr/Employee/Onboarding src change routes to the CoreHr filter' `
        -Files @('src/ONEVO.Application/Features/CoreHr/Onboarding/Queries/ListOnboardingAccessGrantRequests/ListOnboardingAccessGrantRequestsQueryHandler.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~CoreHr|FullyQualifiedName~Employee|FullyQualifiedName~Onboarding' }

    Assert-Decision -Name 'Leave src change routes to Leave filter instead of full integration' `
        -Files @('src/ONEVO.Api/Controllers/Tenant/Leave/LeavePoliciesController.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Leave' }

    Assert-Decision -Name 'Leave integration test file under Features/Leave routes to Leave filter' `
        -Files @('tests/ONEVO.Tests.Integration/Features/Leave/LeavePoliciesIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Leave' }

    Assert-Decision -Name 'CoreHr/Employee/Onboarding integration test file also routes to the CoreHr filter' `
        -Files @('tests/ONEVO.Tests.Integration/CoreHr/OnboardingDraft/OnboardingDraftsIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~CoreHr|FullyQualifiedName~Employee|FullyQualifiedName~Onboarding' }

    Assert-Decision -Name 'Unit-test-only change skips integration (already covered by build-and-test)' `
        -Files @('tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs') `
        -Check { param($d) $d.Skip -eq $true }

    Assert-Decision -Name 'Architecture-test-only change skips integration (already covered by build-and-test)' `
        -Files @('tests/ONEVO.Tests.Architecture/PositionsControllerArchitectureTests.cs') `
        -Check { param($d) $d.Skip -eq $true }

    Assert-Decision -Name 'Unit + Architecture changed together (still no src/, no Integration) skips integration' `
        -Files @(
            'tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs'
            'tests/ONEVO.Tests.Architecture/PositionsControllerArchitectureTests.cs'
        ) `
        -Check { param($d) $d.Skip -eq $true }

    Assert-Decision -Name 'Unit test change + a real Integration test change does NOT skip - Integration side still routes to its area' `
        -Files @(
            'tests/ONEVO.Tests.Unit/Features/CoreHr/Onboarding/ListOnboardingAccessGrantRequestsQueryHandlerTests.cs'
            'tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs'
        ) `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Department' }

    Assert-Decision -Name 'A real ONEVO.Tests.Integration file change (non-Unit/Architecture) routes to its area, not skipped' `
        -Files @('tests/ONEVO.Tests.Integration/OrgStructure/Department/DepartmentsIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Department' }

    # Regression guards for the "area folder sits directly under the Integration project root"
    # bug caught while validating CoreHr above: a naive '*/<Name>/*' pattern silently misses
    # these because there is no leading '/' before <Name> once the fixed
    # 'tests/ONEVO.Tests.Integration/' prefix is consumed. Auth, Monitoring, and Storage all have
    # this exact real-world shape (unlike LegalEntity/Department/Position, which are nested one
    # level deeper under OrgStructure/ and would have passed even with the buggy pattern).
    Assert-Decision -Name 'Auth integration test file directly under the project root routes to Auth (regression guard)' `
        -Files @('tests/ONEVO.Tests.Integration/Auth/TenantSessionRlsIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Auth|FullyQualifiedName~Legal|FullyQualifiedName~Password|FullyQualifiedName~Mfa|FullyQualifiedName~Session' }

    Assert-Decision -Name 'Monitoring integration test file directly under the project root routes to Monitoring (regression guard)' `
        -Files @('tests/ONEVO.Tests.Integration/Monitoring/ActivityMonitoring/ActivityIngestIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Monitoring|FullyQualifiedName~Tray|FullyQualifiedName~CheckIn' }

    Assert-Decision -Name 'Storage integration test file directly under the project root routes to Storage (regression guard)' `
        -Files @('tests/ONEVO.Tests.Integration/Storage/StorageQuotaIntegrationTests.cs') `
        -Check { param($d) -not $d.Skip -and -not $d.FullIntegration -and $d.Filter -eq 'FullyQualifiedName~Storage|FullyQualifiedName~File' }

    if ($failures -gt 0) {
        Write-Host "$failures self-test(s) FAILED"
        exit 1
    }
    Write-Host 'All self-tests passed.'
    exit 0
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
if ($SelfTest) {
    Invoke-SelfTest
    return
}

$inputFiles = @()
if ($PSCmdlet.ParameterSetName -eq 'FilesPath') {
    if (-not (Test-Path -LiteralPath $ChangedFilesPath)) {
        throw "ChangedFilesPath '$ChangedFilesPath' does not exist."
    }
    $inputFiles = Get-Content -LiteralPath $ChangedFilesPath
}
elseif ($ChangedFiles) {
    $inputFiles = $ChangedFiles
}

$normalized = Get-NormalizedPaths -Paths $inputFiles
$decision   = Get-IntegrationDecision -Files $normalized
Write-DecisionOutputs -Decision $decision
return $decision
