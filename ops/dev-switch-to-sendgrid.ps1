# Local-dev helper: deactivates the resend service key and registers a SendGrid key
# as the active transactional email provider. The key is prompted interactively
# (masked), sent only to your local backend, encrypted there (AES-256-GCM), and
# never written to disk or logs by this script.
#
# Prerequisites: backend running on http://localhost:5139, your admin account
# with MFA enrolled, and a SendGrid API key from a Single-Sender-verified account
# (verified sender must match Email:FromAddress in appsettings.Development.json).
#
# Run:  powershell -ExecutionPolicy Bypass -File .\ops\dev-switch-to-sendgrid.ps1

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
$headers = @{ "X-CSRF-Token" = $csrf }

# Step 3: deactivate resend (only one active transactional-email provider allowed at a time)
try {
    $deactivate = Invoke-WebRequest -Uri "$baseUrl/admin/v1/system-config/service-keys/resend/deactivate" `
        -Method Post -WebSession $session -Headers $headers -UseBasicParsing
    Write-Host "Deactivate resend: HTTP $($deactivate.StatusCode)"
} catch {
    Write-Host "Deactivate resend: $($_.Exception.Message) (fine if resend was already inactive)"
}

# Step 4: register the SendGrid key
$apiKey = Read-Masked "SendGrid API key (starts with SG.)"
$keyBody = @{ serviceKey = "sendgrid"; displayName = "SendGrid (local dev)"; apiKey = $apiKey } | ConvertTo-Json
try {
    $create = Invoke-WebRequest -Uri "$baseUrl/admin/v1/system-config/service-keys" -Method Post `
        -ContentType "application/json" -Body $keyBody -WebSession $session -Headers $headers -UseBasicParsing
    Write-Host "Create sendgrid key: HTTP $($create.StatusCode)"
    Write-Host $create.Content
} catch {
    # A sendgrid row may already exist from a prior attempt - rotate instead.
    Write-Host "Create failed ($($_.Exception.Message)), trying rotate-key instead..."
    $rotateBody = @{ apiKey = $apiKey } | ConvertTo-Json
    $rotate = Invoke-WebRequest -Uri "$baseUrl/admin/v1/system-config/service-keys/sendgrid/rotate-key" `
        -Method Post -ContentType "application/json" -Body $rotateBody -WebSession $session -Headers $headers -UseBasicParsing
    Write-Host "Rotate sendgrid key: HTTP $($rotate.StatusCode)"

    $activate = Invoke-WebRequest -Uri "$baseUrl/admin/v1/system-config/service-keys/sendgrid/activate" `
        -Method Post -WebSession $session -Headers $headers -UseBasicParsing
    Write-Host "Activate sendgrid key: HTTP $($activate.StatusCode)"
}

Write-Host ""
Write-Host "Done. Restart the backend (to reread appsettings FromAddress if you just changed it),"
Write-Host "then submit a fresh forgot-password request - the outbox will send via SendGrid."
