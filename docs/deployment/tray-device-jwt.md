# Tray device JWT signing keys (SEC-15)

The Crownus API signs WorkPulse tray device tokens with an **ECDSA P-256 private key (ES256)**.
Every token names its key in the `kid` header, so a new key can be added while tokens signed with
the old key still validate. The tray app never holds a key: it receives an access token at
activation (`/api/v1/monitoring/activation/exchange`), sends it as a bearer token, and only reads
its claims (`sub`, `user_id`, `tenant_id`, `exp`). It never checks the signature. Do not give the
keys to agents or the web front end.

SEC-15 (audit F-17) replaced the earlier single shared HS256 secret (SEC-01).

| Setting | Environment variable | Required | Notes |
|---|---|---|---|
| `TrayApp:Jwt:SigningKeys:<n>:Kid` | `TrayApp__Jwt__SigningKeys__<n>__Kid` | yes, outside Development/Test | Key id written to the token header. Unique, for example `tray-2026-11`. |
| `TrayApp:Jwt:SigningKeys:<n>:PrivateKeyPem` | `TrayApp__Jwt__SigningKeys__<n>__PrivateKeyPem` | yes, with its `Kid` | PEM-encoded P-256 private key (PKCS#8 `BEGIN PRIVATE KEY` or SEC1 `BEGIN EC PRIVATE KEY`). Secret store only. A literal `\n` is read as a line break, so the PEM can sit on one line. |
| `TrayApp:Jwt:ActiveKid` | `TrayApp__Jwt__ActiveKid` | yes, outside Development/Test | The listed key that signs new tokens. Every listed key is accepted for validation. |
| `TrayApp:Jwt:AcceptLegacyHs256Until` | `TrayApp__Jwt__AcceptLegacyHs256Until` | no | Cut-over only. Until this instant (`2026-11-20T12:00:00Z`, with an offset), also accept tokens signed with the old HS256 secret. |
| `TrayApp:Jwt:Secret` | `TrayApp__Jwt__Secret` | only with `AcceptLegacyHs256Until` | The old HS256 secret. Not read otherwise. |
| `TrayApp:Jwt:Issuer` | `TrayApp__Jwt__Issuer` | no | Default `crownus-tray`. |
| `TrayApp:Jwt:Audience` | `TrayApp__Jwt__Audience` | no | Default `crownus-tray-app`. |

## How tokens are checked

- Only ES256 is accepted (plus HS256 while the legacy window is open). This pins the algorithm and
  blocks `alg=none` and algorithm-confusion tricks.
- An ES256 token must carry a `kid` that names a listed key, and must be signed by that key. A token
  with an unknown or missing `kid` is rejected, even if a listed key signed it.
- During the legacy window, a token **without** a `kid` is checked against the old HS256 secret.
  The end time is checked on every request, so the window closes on time without a restart.
- Issuer, audience and lifetime are checked as before (30 s clock skew). Access tokens last 1 hour.
  Refresh tokens are opaque, stored hashed, valid for 90 days and not signed with these keys.

## Startup behaviour

- **Outside Development/Test** (`ASPNETCORE_ENVIRONMENT` = Production, Staging, UAT, ...) the API
  refuses to start when:
  - no key is listed, or `ActiveKid` is missing or not listed
  - a `Kid` is blank or listed twice
  - a PEM is missing, a placeholder (`<...>`, `SET_VIA_ENV`, `CHANGE_ME`, ...), unparseable, a
    public key only, or not P-256
  - `AcceptLegacyHs256Until` is not an explicit instant, or it is set but `TrayApp:Jwt:Secret` is
    missing, shorter than 32 bytes or a `dev-only`/placeholder value

  Errors name the setting and the kid. They never contain key material.
- **Development/Test** with no key configured sign with a **throwaway key** (kid `dev-ephemeral-…`)
  and log a warning. It changes on every restart. The tray then gets a 401 on its next call and
  silently refreshes (see below). Configured keys are still checked in Development.

## Generating a key

Generate the key on the server (or in the secret store), never in chat or in the repository.

```bash
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out tray-key.pem
```

`openssl` ships with Git for Windows (`C:\Program Files\Git\usr\bin\openssl.exe`). To put the
PEM on one line for `web.config` or an environment variable, run this in Windows PowerShell:

```powershell
(Get-Content .\tray-key.pem) -join '\n'
```

The `'\n'` is a literal backslash and `n`, not a line break. Delete `tray-key.pem` once the value is
in the secret store.

## First deploy of SEC-15 (switch from HS256)

Tray apps already in the field need **no update**: they only read the claims, which did not change.
When an old HS256 access token is rejected with 401, the tray calls `/activation/refresh` (anonymous,
uses the refresh token) and gets an ES256 token. It does not need to re-enrol. Pick one of these:

- **With a window (recommended for the first production deploy):** keep `TrayApp__Jwt__Secret` set
  to the current secret, and set `TrayApp__Jwt__AcceptLegacyHs256Until` to at least 1 hour after the
  planned deploy time. After that time, remove both settings and restart.
- **Hard switch:** do not set the window. Each tray refreshes once on its next call. Uploads that
  get a 401 stay pending in the tray's buffer and are sent after the refresh.

## Key rotation runbook

Rotate on a schedule, and right away after any suspected exposure of a private key.

1. **Add** the new key as the next `SigningKeys` entry (new `Kid` + `PrivateKeyPem`) and deploy.
   Both keys are accepted; the old key still signs.
2. **Activate:** set `TrayApp__Jwt__ActiveKid` to the new kid and deploy. New tokens use the new key.
3. **Wait at least 1 hour** (the access-token lifetime), so every token signed with the old key has
   expired.
4. **Remove** the old key's entry and deploy. Keep the remaining entries numbered from `0`
   (`SigningKeys__0__…`).

On a suspected key leak, do steps 1 and 2 together, then step 4 right away instead of waiting.
Trays holding old tokens will refresh once.

## Proxy and server logs: redact `access_token`

SignalR WebSockets cannot send an `Authorization` header, so the tray connects to
`/hubs/agent-commands?access_token=<token>`. The API's own request log records the path only, but
reverse proxies and web servers log the full URL by default. A valid token in a log file can be
replayed until it expires (at most 1 hour).

- **IIS (with or without ARR)**, on every site the hub request passes through (the public site that
  ARR fronts and the API site): IIS Manager → site → **Logging** → **Select Fields...** → untick
  **URI Query (cs-uri-query)** → **Apply**. Or from an elevated prompt:

  ```powershell
  & "$env:windir\system32\inetsrv\appcmd.exe" set site "<site>" "/logFile.logExtFileFlags:Date,Time,ClientIP,UserName,ServerIP,Method,UriStem,HttpStatus,Win32Status,TimeTaken,ServerPort,UserAgent,HttpSubStatus,Referer"
  ```

  This keeps the default fields except `UriQuery`. The command has not yet been tried on the
  Crownus server; if it fails, use the IIS Manager steps above. Either way, check that the next log
  lines have no query.
- **nginx:** log `$uri` (no query) for the hub location:

  ```nginx
  log_format no_query '$remote_addr - $remote_user [$time_local] "$request_method $uri $server_protocol" '
                      '$status $body_bytes_sent "$http_referer" "$http_user_agent"';
  location /hubs/agent-commands {
      access_log /var/log/nginx/access.log no_query;
      # proxy_pass and WebSocket upgrade headers as usual
  }
  ```
- **Load balancers and CDNs:** turn off query-string logging for `/hubs/agent-commands`, or redact
  the `access_token` parameter.

Also delete or restrict log files written before this change. They may hold tokens, but any such
token has expired within an hour of being logged.
