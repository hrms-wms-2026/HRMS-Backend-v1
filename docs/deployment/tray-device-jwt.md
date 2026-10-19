# Tray device JWT secret (SEC-01)

The ONEVO API signs and validates WorkPulse tray device tokens with one HS256 key. The tray app
never holds it: it receives an access token at activation (`/api/v1/monitoring/activation/exchange`)
and sends it as a bearer token. Do not distribute this secret to agents or the web front end.

| Setting | Environment variable | Required | Notes |
|---|---|---|---|
| `TrayApp:Jwt:Secret` | `TrayApp__Jwt__Secret` | yes | Random, at least 32 bytes (HS256). Secret store only. |
| `TrayApp:Jwt:Issuer` | `TrayApp__Jwt__Issuer` | no | Default `onevo-tray`. |
| `TrayApp:Jwt:Audience` | `TrayApp__Jwt__Audience` | no | Default `onevo-tray-app`. |

## Startup behaviour

- **Outside Development/Test** (`ASPNETCORE_ENVIRONMENT` = Production, Staging, UAT, ...) the API
  refuses to start when the secret is missing, shorter than 32 bytes, or a placeholder
  (`dev-only…`, `<…>`, `CHANGE_ME`, `PLACEHOLDER`, `SET_VIA_ENV`). The error names the setting,
  never the value.
- **Development/Test** may use the self-labeled value in `appsettings.Development.json` or the
  `.env` file; it is checked lazily by the token issuer and the `TrayDeviceScheme` handler.

## Deploying

1. Generate a value: `openssl rand -base64 48`.
2. Store it in the platform secret store and expose it to the API process as `TrayApp__Jwt__Secret`.
3. Start the API; a placeholder or short key stops startup with an `InvalidOperationException`.

## Rotation

Rotating the key invalidates every issued tray access token. Agents keep their buffered data
(rows stay pending on 401) and get a new access token from `/activation/refresh` — refresh tokens
are opaque, stored hashed in the database and not signed with this key, so no re-activation is needed. Rotate after any
suspected exposure; the tracked development placeholder is not a production credential.
