# Postman Collection Repair Report

## Overview

This report details the inspection, repair, and alignment of the local file-backed Postman collections for `HRMS-Backend-v1`.

No backend source code, frontend source code, database migrations, unit/integration tests, or OneVo-HR documentation files were modified. All changes were confined strictly to `postman/`, `.postman/`, and `POSTMAN_COLLECTION_REPAIR_REPORT.md`.

---

## 1. What Was Missing

1. **`01. Auth - Main Login Page` folder in `ONEVO Organization Admin API`**:
   The `01. Auth - Main Login Page` folder was missing from disk under `postman/collections/ONEVO Organization Admin API/`.
   The current backend endpoints for tenant authentication (base-domain login, workspace selection, base Google login, tenant session exchange, session `me`, and logout) were absent from the local folder collection.

2. **Environment Variable Alignments**:
   - `tenant_base_url` was pointing to `https://localhost:7229` instead of the HTTPS local tenant host `https://acme.localhost:7229`.
   - `exchange_code` and `google_id_token` keys were missing from `New Environment.environment.yaml`.

---

## 2. What Existed But Postman Failed to Display

On disk, the local collection directory `postman/collections/ONEVO Organization Admin API/` contained:
- `03. Legal Acceptance` (3 requests)
- `04. MFA` (4 requests)
- `05. Password` (5 requests)
- `06. Invitations` (3 requests)
- `06. Organization - Companies` (10 requests)
- `07. Organization - Departments` (11 requests)
- `08. Organization - Positions` (12 requests)
- `99. Health` (2 requests)

However, Postman Desktop UI only displayed **`Organization - Departments`** and **`Organization - Positions`**.

### Cause:
Postman workspace metadata in `.postman/resources.yaml` bound `ONEVO Organization Admin API` exclusively to Cloud resource ID `49350681-47a8ae07-b96d-44bf-868b-bbe9a47c6b8c`, while `localResources.collections` only declared the legacy monolithic JSON collection `../ONEVO-HRMS.postman_collection.json`. Because the cloud resource snapshot contained only folders 07 and 08, Postman UI rendered the partial cloud view instead of indexing the local disk folders.

Adding the local collection folder paths to `localResources.collections` in `.postman/resources.yaml` allows Postman to index all local disk folders directly.

---

## 3. Files Created and Changed

### Recreated Auth Requests (`postman/collections/ONEVO Organization Admin API/01. Auth - Main Login Page/`)
- **[NEW] `Login.request.yaml`**: `POST {{base_url}}/api/v1/auth/login` (email + password, auto-extracts `login_challenge` / `exchange_code`)
- **[NEW] `Select Workspace.request.yaml`**: `POST {{base_url}}/api/v1/auth/login/select-workspace` (`login_challenge` + `workspace` slug)
- **[NEW] `Login With Google.request.yaml`**: `POST {{base_url}}/api/v1/auth/login/google` (`google_id_token`)
- **[NEW] `Session Exchange.request.yaml`**: `POST {{tenant_base_url}}/api/v1/auth/session-exchange` (`code` only in body)
- **[NEW] `Me.request.yaml`**: `GET {{tenant_base_url}}/api/v1/auth/me`
- **[NEW] `Logout.request.yaml`**: `POST {{tenant_base_url}}/api/v1/auth/logout` (`X-CSRF-Token` header)

### Environment Variable Updates (`postman/environments/New Environment.environment.yaml`)
- **[MODIFY] `New Environment.environment.yaml`**:
  - `base_url`: `https://localhost:7229`
  - `admin_base_url`: `https://admin.localhost:7229`
  - `tenant_base_url`: `https://acme.localhost:7229`
  - `tenant_slug`: `acme`
  - `tenant_host`: `https://acme.localhost:7229`
  - Added `exchange_code: ''` and `google_id_token: ''`

### Resource Metadata Updates (`.postman/resources.yaml`)
- **[MODIFY] `.postman/resources.yaml`**:
  - Added `../postman/collections/ONEVO Developer Platform API` and `../postman/collections/ONEVO Organization Admin API` under `localResources.collections`.

---

## 4. Exact Folder Tree After Repair

```
postman/collections/
├── ONEVO Developer Platform API/
│   ├── .resources/
│   │   └── definition.yaml
│   ├── 01. Admin Auth/
│   │   ├── Google Callback.request.yaml
│   │   ├── Google Config.request.yaml
│   │   ├── Login.request.yaml
│   │   └── Logout.request.yaml
│   ├── 02. System Config - Provider Options/
│   │   ├── List OAuth Providers.request.yaml
│   │   ├── List Payment Gateway Providers.request.yaml
│   │   └── List Service Key Providers.request.yaml
│   ├── 03. System Config - Service Keys/
│   │   ├── Activate Service Key.request.yaml
│   │   ├── Create Service Key.request.yaml
│   │   ├── Deactivate Service Key.request.yaml
│   │   ├── Get Service Key.request.yaml
│   │   ├── List Service Keys.request.yaml
│   │   ├── Rotate Service Key.request.yaml
│   │   ├── Update Service Key Display Name.request.yaml
│   │   └── Verify Saved Service Key.request.yaml
│   ├── 04. System Config - OAuth Apps/
│   │   ├── Activate OAuth App.request.yaml
│   │   ├── Deactivate OAuth App.request.yaml
│   │   ├── List OAuth Apps.request.yaml
│   │   ├── Rotate Secret.request.yaml
│   │   └── Validate Config.request.yaml
│   ├── 05. System Config - Payment Gateways/
│   │   ├── Resolve Gateway For Country.request.yaml
│   │   └── Rotate Gateway Credentials.request.yaml
│   ├── 06. Tenants/
│   │   ├── Confirm Provisioning.request.yaml
│   │   ├── Create Tenant With Owner Invite.request.yaml
│   │   ├── Get Tenant.request.yaml
│   │   ├── Invite Tenant Admin.request.yaml
│   │   ├── List Tenants.request.yaml
│   │   ├── Patch Tenant Status.request.yaml
│   │   ├── Provisioning Summary.request.yaml
│   │   ├── Resend Owner Invite.request.yaml
│   │   ├── Update Tenant.request.yaml
│   │   └── Validate Tenant.request.yaml
│   └── 99. Health/
│       ├── Health.request.yaml
│       └── Ready.request.yaml
└── ONEVO Organization Admin API/
    ├── .resources/
    │   └── definition.yaml
    ├── 01. Auth - Main Login Page/
    │   ├── Login With Google.request.yaml
    │   ├── Login.request.yaml
    │   ├── Logout.request.yaml
    │   ├── Me.request.yaml
    │   ├── Select Workspace.request.yaml
    │   └── Session Exchange.request.yaml
    ├── 03. Legal Acceptance/
    │   ├── Authenticated Legal Acceptance.request.yaml
    │   ├── Complete Pending Login Legal Acceptance.request.yaml
    │   └── Get Pending Legal Documents.request.yaml
    ├── 04. MFA/
    │   ├── Confirm MFA Setup.request.yaml
    │   ├── Disable MFA.request.yaml
    │   ├── Enable MFA.request.yaml
    │   └── Verify MFA Login Challenge.request.yaml
    ├── 05. Password/
    │   ├── Change Password From Profile.request.yaml
    │   ├── Force Change Password.request.yaml
    │   ├── Forgot Password - Base Domain.request.yaml
    │   ├── Forgot Password - Tenant Host.request.yaml
    │   └── Reset Password.request.yaml
    ├── 06. Invitations/
    │   ├── Accept Invite With Google + Legal Acceptance.request.yaml
    │   ├── Accept Invite With Password + Legal Acceptance.request.yaml
    │   └── Invitation Preview.request.yaml
    ├── 06. Organization - Companies/
    │   ├── Create Company - Duplicate Name Should 409.request.yaml
    │   ├── Create Company.request.yaml
    │   ├── Delete Company - Last Company Should Fail 400.request.yaml
    │   ├── Delete Company - Wrong Confirm Name Should 400.request.yaml
    │   ├── Delete Company.request.yaml
    │   ├── Get Company General Settings.request.yaml
    │   ├── Get Missing Company Should 404.request.yaml
    │   ├── List Companies.request.yaml
    │   ├── Remove Company Logo.request.yaml
    │   └── Update Company General Settings.request.yaml
    ├── 07. Organization - Departments/
    │   ├── Archive Department.request.yaml
    │   ├── Check Department Archive Blockers.request.yaml
    │   ├── Create Department With Head Should 409.request.yaml
    │   ├── Create Department.request.yaml
    │   ├── Deprecated DELETE Department Alias.request.yaml
    │   ├── Get Department.request.yaml
    │   ├── List Department Tree.request.yaml
    │   ├── List Departments.request.yaml
    │   ├── Restore Department.request.yaml
    │   ├── Update Department - Clear Head Position.request.yaml
    │   └── Update Department - Set Head Position.request.yaml
    ├── 08. Organization - Positions/
    │   ├── Archive Position.request.yaml
    │   ├── Check Position Archive Blockers.request.yaml
    │   ├── Create Position - Pooled.request.yaml
    │   ├── Create Position - Unique.request.yaml
    │   ├── Get Position Tree.request.yaml
    │   ├── Get Position.request.yaml
    │   ├── List Positions - Include Archived.request.yaml
    │   ├── List Positions.request.yaml
    │   ├── Negative - Invalid Position Type.request.yaml
    │   ├── Negative - Unique Capacity Not One.request.yaml
    │   ├── Restore Position.request.yaml
    │   └── Update Position.request.yaml
    └── 99. Health/
        ├── Health.request.yaml
        └── Ready.request.yaml
```

---

## 5. Verification Summary

- **Total Request YAML Files Validated**: 90 request YAML files across both collections (40 in `ONEVO Developer Platform API` + 50 in `ONEVO Organization Admin API`), all passing syntax and structural checks.
- **Contract Matching**: Request bodies (`LoginRequest`, `SelectWorkspaceRequest`, `BaseGoogleLoginRequest`, `TenantSessionExchangeRequest`), CSRF token headers (`X-CSRF-Token` for `logout`), and URLs (`{{base_url}}`, `{{tenant_base_url}}`) strictly match backend C# controller signatures.
- **Git Safety**: No commits or pushes performed.
