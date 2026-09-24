# Submit Legal Acceptances

**POST** `/api/v1/legal/acceptances`

**Auth:** `[Authorize(Policy = "TenantPolicy")]` (whole `LegalController` requires it). **CSRF:** required.

## Description

Submits legal acceptance decisions for an **already-authenticated** tenant user — the counterpart mutation to Get Pending Legal Documents.

## Request

```json
{
  "acceptances": [
    {
      "document_type": "privacy",
      "version": "2026-02-01",
      "decision": "accepted",
      "content_hash": "sha256 hash of the displayed document content (optional)"
    }
  ]
}
```

## Response

`200 OK`:

```json
{ "success": true }
```

## Errors

| Status | Cause |
|---|---|
| `400` | A required document not accepted, or invalid `document_type`/`version` |
| `401` | Not authenticated |

## Source

`src/ONEVO.Api/Controllers/Tenant/Legal/LegalController.cs` (`SubmitAcceptances`)
Handler: `SubmitLegalAcceptanceCommandHandler` (`src/ONEVO.Application/Features/Auth/Legal/Commands/SubmitLegalAcceptance/`)
