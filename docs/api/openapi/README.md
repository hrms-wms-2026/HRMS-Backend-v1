# OpenAPI documents

Generated at build time from the controllers (task 23). The API serves no Swagger UI and no `/swagger` or `/openapi` endpoint in any environment; these files are the only published API description.

| File | Audience | Routes |
|---|---|---|
| `tenant-v1.json` | Tenant web app and tray app | everything except `admin/v1` |
| `admin-v1.json` | Developer platform admin console | `admin/v1` only |

Both are OpenAPI 3.1 and list the real sign-in methods as security schemes: the HttpOnly session cookie (`crownus_session` or `admin_session`) plus the `X-CSRF-Token` header on unsafe methods, and for the tenant document the tray app's bearer token.

## Uses

- **Postman:** Import → File, pick a file, import it as a collection. See `docs/postman-request/README.md`.
- **Frontend TypeScript types:** generate them from these files.

## Keeping them current

Every `dotnet build` of `src/ONEVO.Api` writes fresh copies to `src/ONEVO.Api/obj/openapi/` (`crownus-api.json` and `crownus-api_admin-v1.json`). The unit test `OpenApiDocumentsAreCurrentTests` fails when the committed files here differ from those. After changing endpoints, refresh and commit:

```
dotnet build src/ONEVO.Api/ONEVO.Api.csproj -p:UpdateOpenApiDocs=true
```

Generation needs no database and no secrets: `Program.cs` skips its startup checks and hosted services when it runs under the build-time generator (`OpenApiDocumentExtensions.IsGeneratingDocuments`).

## Known gap

Most actions return `IActionResult` without `[ProducesResponseType]`, so their 2xx responses have no schema. Request bodies are typed. The counts are in `docs/reports/security/2026-11-18-task-23-headers-uploads-limits.md`.
