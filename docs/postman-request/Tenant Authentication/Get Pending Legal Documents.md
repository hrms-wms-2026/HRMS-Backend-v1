# Get Pending Legal Documents

**GET** `/api/v1/legal/pending`

**Auth:** `[Authorize(Policy = "TenantPolicy")]` (whole `LegalController` requires it). **CSRF:** n/a (GET).

## Description

Post-login check for an **already-authenticated** tenant user: are there any legal documents (new versions, newly-required types) they haven't accepted yet. Distinct from the mid-login gate (Complete Pending Legal Acceptance) — this is for documents that become pending after a session already exists.

## Request

No body.

## Response

`200 OK`, `LegalAcceptanceCheckResult`:

No `JsonStringEnumConverter` is registered in this API, so `status` serializes as its **numeric** enum value, not a string.

```json
{
  "status": 1,
  "isComplete": false,
  "pendingDocuments": [
    {
      "document_type": "privacy",
      "version": "2026-02-01",
      "title": "Privacy Policy",
      "effective_at": "2026-02-01T00:00:00Z",
      "content_url": null,
      "content_endpoint": "/api/v1/legal/documents/privacy/2026-02-01/content",
      "content_hash": "sha256..."
    }
  ],
  "errorCode": null
}
```

`status` values: `0` = Complete, `1` = Pending, `2` = NotConfigured (`LegalAcceptanceStatus` enum).

## Errors

| Status | Cause |
|---|---|
| `400` | Tenant context not resolved |
| `401` | Not authenticated |

## Source

`src/ONEVO.Api/Controllers/Tenant/Legal/LegalController.cs` (`GetPendingLegalDocuments`)
Service: `ILegalAcceptanceChecker.CheckAsync` (`src/ONEVO.Application/Features/Auth/Legal/Services/ILegalAcceptanceChecker.cs`)
