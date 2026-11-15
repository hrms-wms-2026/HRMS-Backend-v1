# Task 43: Feature keys and module gates that are enforced

| | |
|---|---|
| Priority | Numbered 43, but do it before task 16. Feature keys are the chosen method for managing API changes (decided 2026-11-15) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day for the design and the Work Management module. Other modules are follow-up days |
| Area | API, Security, Architecture |
| Depends on | 32 (permission catalogue). Task 16 depends on this one |

## What we found

The project has three separate ways to switch an API on or off. They were checked on 2026-11-15.

**1. Module keys (enforced).**

- `[RequireAnyModule(...)]` checks that the tenant has at least one of the listed modules active. It is used 20 times, only in Work Management, and every use lists the same six keys: `worksync_foundation`, `tasks`, `projects`, `planning_sprints`, `objectives_milestones`, `boards`.
- `PermissionResolver` also drops any permission whose module is not active for the tenant. That covers the other modules indirectly.
- The filter calls `IModuleEntitlementService.GetActiveModuleKeysForTenantAsync` on every request. The permission resolver calls the same method again for the same request.
- The 403 message says "An active Work Management module is required" whatever the module.

**2. Feature keys (stored, not enforced).**

- `module_features` holds 192 rows: a `feature_key` under each `module_key`, with `is_default_included` and `is_active`.
- The only code that reads feature keys is the seeder and one admin catalogue query (`GetModuleCatalogFeaturesQueryHandler`). No endpoint, handler or filter checks a feature key. Switching a feature off in the catalogue changes nothing in the API.
- `feature_access_grants` (grant a module to a role or an employee) has an entity, a repository and a DI registration. Nothing calls the repository, and the table is empty.

**3. Settings flags (enforced, global).**

- `Meetings:Create`, `Meetings:BulkInvite`, `Meetings:Background`, `Meetings:ReadOnly` and `Planner:Enabled` come from `appsettings` and are read through `MeetingsOptions` and `PlannerOptions` inside handlers.
- They apply to every tenant at once and need a restart to change.
- The frontend has its own `core/config/feature-flags.ts` with two flags, which the cleanup scan reports as unused.

So a feature key is not a way to manage API versions or API availability today. Only module keys and the settings flags gate anything.

## Decision (2026-11-15)

Feature keys are the method the team uses to release and manage APIs. A new or changed API goes behind a feature key and is switched on per tenant. URL versions are kept only for breaking changes on endpoints the tray app calls. The full rule table is in task 16.

## What to do

1. Write the rule in `docs/api/feature-gating.md`. Each mechanism has one job:
   - module key: the tenant bought it;
   - feature key: a part of a module is switched on for this tenant;
   - permission: this user may do it;
   - settings flag: a release switch for the whole installation, removed once the feature is stable;
   - API version: only for a breaking contract change on a tray endpoint (task 16). A feature key must not make one URL return two different response shapes.
2. Add one attribute, `[RequireFeature("feature_key")]`, backed by a per-request cached entitlement lookup. It checks the feature is active and its module is active for the tenant.
3. Replace the six-key list repeated 20 times with one named constant or a single `[RequireModuleGroup(WorkManagement)]`, and make the 403 message name the missing module.
4. Cache the active module and feature keys once per request (and per tenant for a short time, see task 22), so the filter and the permission resolver do not both query.
5. Put module keys and feature keys in the same code catalogue as permissions (task 32), so a key is a constant and an unknown key fails the build.
6. Decide what `feature_access_grants` is for. If per-role or per-employee module access is wanted, wire it into the entitlement check. If not, remove the table, entity and repository (task 42).
7. Return the active module and feature keys in `auth/me`, and let the frontend hide menus and routes from that list. Delete the unused `feature-flags.ts` or replace it with the server list.
8. Move the `Meetings:*` and `Planner:Enabled` switches to feature keys if they must differ per tenant. Otherwise leave them and note the date they should be removed.
9. Add an architecture test: every controller under a module folder carries a module or feature gate, or is on an allow-list.

## How to release an API with a feature key (write this into the doc)

1. Add the key to the code catalogue and to `ModuleCatalogSeeder` under its module, with `is_default_included = false`.
2. Put `[RequireFeature(Features.X)]` on the new controller or action. For changed behaviour inside an existing handler, branch on the same key.
3. Add the key to the frontend generated file and hide the menu, route and buttons behind it.
4. Switch the key on for the test tenant, then for pilot tenants, then for all.
5. When every tenant has it on, set `is_default_included = true`, remove the check and the old code path, and delete the key.
6. Each key has an owner and a removal date in `docs/api/feature-keys.md`.

Also add a platform admin endpoint and screen to switch a feature key on or off per tenant, with an audit entry for each change. Today the catalogue can be read but a per-tenant switch is not enforced anywhere.

## Done when

- A per-tenant feature switch exists in the admin console and every change is audited.
- Switching a feature key off for a tenant makes its endpoints return 403 with a message that names the feature (test).
- Work Management controllers use one gate definition, not a repeated list.
- One request makes at most one entitlement query.
- `feature_access_grants` has a decision.
- The gating doc is merged.
