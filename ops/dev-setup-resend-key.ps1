# Local-dev helper: registers your Resend API key as the active transactional
# email provider via the backend admin API. The key is prompted interactively
# (masked), sent only to your local backend, encrypted there (AES-256-GCM),
# and never written to disk or logs by this script.
#
# Prerequisites: backend running on http://localhost:5139, your admin account
# with MFA enrolled, and your authenticator app for the 6-digit code.
#
# Run:  powershell -ExecutionPolicy Bypass -File .\ops\dev-setup-resend-key.ps1

$baseUrl = "http://localhost:5139"

function Read-Masked([string]$prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

$email = Read-Host -Prompt "Admin email [dapiyshanth1908@gmail.com]"
if ([string]::IsNullOrWhiteSpace($email)) { $email = "dapiyshanth1908@gmail.com" }
$password = Read-Masked "Admin password"

# Step 1: login (expects 202 = MFA challenge issued as admin_mfa cookie)
$loginBody = @{ email = $email; password = $password } | ConvertTo-Json
$login = Invoke-WebRequest -Uri "$baseUrl/admin/v1/auth/login" -Method Post `
    -ContentType "application/json" -Body $loginBody -SessionVariable session -UseBasicParsing
Write-Host "Login: HTTP $($login.StatusCode) (202 = MFA required, expected)"

# Step 2: MFA verify (issues admin_session + admin_csrf cookies)
$code = Read-Host -Prompt "6-digit MFA code from your authenticator app"
$mfaBody = @{ code = $code } | ConvertTo-Json
$mfa = Invoke-WebRequest -Uri "$baseUrl/admin/v1/auth/mfa/verify" -Method Post `
    -ContentType "application/json" -Body $mfaBody -WebSession $session -UseBasicParsing
Write-Host "MFA verify: HTTP $($mfa.StatusCode)"

$csrf = ($session.Cookies.GetCookies($baseUrl) | Where-Object { $_.Name -eq "admin_csrf" }).Value
if ([string]::IsNullOrWhiteSpace($csrf)) { throw "admin_csrf cookie not found - MFA verify did not succeed." }

# Step 3: register the Resend key (encrypted server-side before persistence)
$apiKey = Read-Masked "Resend API key (starts with re_)"
$keyBody = @{ serviceKey = "resend"; displayName = "Resend (local dev)"; apiKey = $apiKey } | ConvertTo-Json
$create = Invoke-WebRequest -Uri "$baseUrl/admin/v1/system-config/service-keys" -Method Post `
    -ContentType "application/json" -Body $keyBody -WebSession $session `
    -Headers @{ "X-CSRF-Token" = $csrf } -UseBasicParsing
Write-Host "Create service key: HTTP $($create.StatusCode)"
Write-Host $create.Content
Write-Host ""
Write-Host "Done. The outbox will auto-retry pending reset emails within ~1-4 minutes."
