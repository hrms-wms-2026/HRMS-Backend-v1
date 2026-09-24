# Complete Pending Legal Acceptance

**POST** `/api/v1/legal/acceptances/complete-login`

**Auth:** `AllowAnonymous`, but requires the `onevo_legal_pending` cookie (set when a prior Login/etc. response had `legal_acceptance_required: true`). **CSRF:** required manually — read `onevo_legal_csrf` cookie, send as `X-CSRF-Token` header (this path is exempt from the normal CSRF middleware and validated in-action instead).

## Description

Accepts the pending legal documents mid-login (the gate between MFA and final session issuance), then continues finalizing the login.

## Request

```json
{
  "acceptances": [
    {
      "document_type": "terms",
      "version": "2026-01-01",
      "decision": "accepted",
      "content_hash": "sha256 hash of the displayed document content (optional)"
    }
  ]
}
```

## Response

Same branches as Login's post-continuation result — typically `200 OK` with `AuthSessionViewModel` once every required document is accepted. See `_Shared - Session Result Response.md`. On success, `onevo_legal_pending`/`onevo_legal_csrf` cookies are cleared.

## Errors

| Status | Cause |
|---|---|
| `401` | `onevo_legal_pending` cookie missing/expired |
| `403` | Missing/invalid `X-CSRF-Token` |
| `400` | A required document not accepted |

## Source

`src/ONEVO.Api/Controllers/Tenant/Auth/AuthPendingLegalController.cs` (`AcceptPendingLegalDocuments`)
`src/ONEVO.Api/Contracts/Auth/AcceptPendingLegalDocumentsRequest.cs`
Handler: `AcceptPendingLegalDocumentsCommandHandler` (`src/ONEVO.Application/Features/Auth/Legal/Commands/AcceptPendingLegalDocuments/`)
