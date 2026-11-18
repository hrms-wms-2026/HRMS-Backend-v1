# Task 16: API versioning

| | |
|---|---|
| Priority | 16 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | API, Architecture |
| Depends on | 43 (feature keys). Do task 43 first |

## Decision (2026-11-15)

The team manages API changes with **feature keys**. A new or changed API is released behind a feature key and switched on per tenant. A new URL version (`v2`) is the exception, used only when a contract must break for a client that cannot be updated at the same time.

This file was rewritten on 2026-11-15 to follow that decision. The feature key work itself is task 43.

## What we found

- There are 103 route attributes that start with the literal text `api/v1` and 18 with `admin/v1`. No versioning library is installed. Swagger has one document per audience, not per version.
- Some actions use absolute routes such as `~/api/v1/work/projects/...` and one Stripe route has no version at all.
- Feature keys exist (192 rows in `module_features`) but no endpoint enforces them yet. Task 43 adds `[RequireFeature]`.
- The tray app runs on employee machines and cannot be force-updated. The web frontend is deployed together with the API, so it can always move at the same time.

## How API changes are managed

| Kind of change | Method |
|---|---|
| New endpoint or new screen | Put it behind a new feature key. Switch it on per tenant when ready |
| New optional field in a request or response | Add it in place. No key and no version needed |
| Changed behaviour of an existing endpoint, same contract | Put the new behaviour behind a feature key inside the handler. Old behaviour stays the default until every tenant is switched |
| Replacing an endpoint | Add the new endpoint behind a feature key. Mark the old one deprecated. Remove it when no tenant has the old key on |
| Removing or renaming a field, changing a type or a status code, for the web app only | Release API and frontend together behind one feature key |
| The same, on an endpoint the tray app calls | New URL version. A feature key is not enough, because old tray builds keep calling the old shape |

Two rules keep this safe:

1. A feature key decides whether an endpoint or behaviour is available. It must not make one URL return two different response shapes. If the shape must differ, it is a new endpoint (or a new version), with its own key.
2. Every feature key used as a release switch has an owner and a removal date. When all tenants are on, the key check and the old code path are deleted.

## What to do

1. Finish task 43 first, so `[RequireFeature]` exists and keys are constants.
2. Write the table and the two rules above in `docs/api/api-change-management.md`, with one worked example of each row.
3. Add a shared route constant for the prefix (`api/v1`, `admin/v1`) and replace the 121 literals with it. This changes no URL. It only removes the repeated text, so a second version later is a small change.
4. Mark the tray-facing controllers clearly (an attribute or a base class such as `TrayApiController`). List them in the doc. These are the only endpoints where a breaking change needs a new version.
5. For those tray endpoints only, add `Asp.Versioning.Mvc` with URL segment versioning, default version 1.0 and `ReportApiVersions = true`. Do not add version attributes to the rest of the API.
6. Add a deprecation helper: an attribute that sends `Deprecation` and `Sunset` headers and logs each call with tenant and client, so the team can see who still uses an old endpoint before removing it.
7. Add an architecture test: no literal `api/v1` in a route attribute, and every tray controller declares its version.
8. Add a release checklist line: "Does this change break a contract? If yes, which row of the table applies?"

## Done when

- The change management doc is merged and linked from the backend README.
- No route attribute contains the literal prefix.
- Tray endpoints report their supported versions in the response headers.
- A deprecated endpoint sends the two headers and its calls appear in the logs.
- All tests pass with no URL changed.
