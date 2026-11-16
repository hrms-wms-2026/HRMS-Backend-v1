# Feature-Key Entitlement and Management: Two-Phase Design

**Date:** 2026-11-16  
**Status:** approved by Dabi on 2026-11-16 (DEC-038); implementation plan next  
**Owner:** Dabi  
**Decision:** DEC-032 (approach), DEC-037 (status rule), DEC-038 (approval) in `onexso/todo/DECISIONS.md`

## 1. Intent

Crownus needs one trustworthy answer to: **may this tenant use this product feature now?** The backend must enforce that answer, independently from the signed-in user's RBAC permission to perform an action.

The current module catalog and namespaced feature rows remain the foundation. Delivery is split into:

1. **Phase 1 — UAT/security-ready entitlement and enforcement.** Make feature decisions centralized, tenant-safe, fail-closed and testable.
2. **Phase 2 — Platform Admin operations.** Add controlled lifecycle management, audited tenant overrides, emergency force-disable, history and versioning.

## 2. Goals, non-goals and anti-goals

### Phase 1 goals

- One service returns a deterministic and explainable effective feature decision for a tenant.
- HTTP endpoints, dashboard providers and background entry points can require a feature key and fail closed.
- Module subscription remains the outer boundary: a feature can never enable an unowned module.
- Existing tenants retain default-included behaviour inside selected active modules.
- Tenant decisions are normalized, RLS-protected and referentially valid.
- Unknown, inactive, malformed and cross-module keys are denied.
- Tests prove enforcement, isolation, cache behaviour and migration compatibility.
- Every active customer-facing catalog feature has backend enforcement. A metadata-only/internal entry may be excluded only with an individual documented reason and security review.

### Phase 2 goals

- Authorized Platform Admin operators manage feature lifecycle and tenant decisions without database editing.
- Every change records actor, reason, correlation/trace reference, safe before/after state and outcome.
- A global emergency force-disable acts predictably and can be safely restored.
- Optimistic concurrency prevents lost updates.
- Operators can inspect history and restore a prior safe configuration as a new version.

### Non-goals

- Percentage rollout, A/B tests, experimentation metrics or user cohorts.
- Replacing RBAC permissions or resource-level authorization.
- Enabling a feature when its tenant subscription does not include the owning module.
- Treating frontend visibility as a security boundary.
- Making Platform Administration a sellable product; it remains an internal operating layer.

### Anti-goals

- Ad-hoc feature queries copied across handlers.
- A missing service, cache failure or malformed configuration granting access.
- Renaming, reusing or physically deleting an issued key.
- One tenant reading or changing another tenant's decisions.
- Secrets or personal data in feature audit history.
- A global kill switch becoming a global enable switch.

## 3. Current state

- `ModuleCatalogItem` and `ModuleFeature` provide module/feature metadata. Seeded keys are namespaced, for example `integrations.api_access`.
- `ModuleEntitlementService` reads `TenantSubscription.SelectedModulesJson` and evaluates module keys only; it does not join active catalog rows.
- `RequireAnyModuleAttribute` protects selected routes at module level.
- `ModuleCatalogController` and the Platform Admin Angular service expose GET operations only.
- `FeatureAccessGrant` stores a `Module` string for a role/employee and has no discovered application/API consumer. It is not feature entitlement.
- Runtime-flag/tenant-override permission constants exist, but no complete flag model, resolver, API or UI was found.

## 4. Approaches considered

| Approach | Trade-off | Verdict |
|---|---|---|
| Add conditions to individual endpoints | Quick but inconsistent, hard to audit and impossible to prove complete | Rejected |
| Build full experimentation tooling now | Powerful but excessive for Release 1 | Rejected |
| Buy a flag provider immediately | Does not replace Crownus subscription, RLS, RBAC and audit boundaries | Deferred |
| **Two-phase native entitlement then operational management** | Reuses the catalog and closes security gaps before operator tooling | **Chosen — DEC-032** |

## 5. Responsibility boundaries

| Concept | Meaning | Authoritative layer |
|---|---|---|
| Module entitlement | Tenant subscription includes a module | Subscription/module entitlement |
| Feature definition | Stable capability inside one module | Module catalog |
| Tenant feature decision | Explicit tenant exception to the feature default | Feature entitlement |
| Global emergency state | Force-disable for all tenants | Platform operations |
| RBAC permission | User may perform an action | Permission/role system |

Effective access requires all applicable layers. Feature entitlement never substitutes for RBAC.

## 6. Key governance and lifecycle

- Canonical keys are lowercase and namespaced as `module.feature`.
- Enforce a documented format and database length. Proposed format: `^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$`.
- `FeatureKey` and owning `ModuleKey` are immutable after creation.
- Keys are never physically deleted or reused for another meaning.
- Lifecycle is `draft`, `active`, `deprecated`, `retired`.
- Deprecated keys remain evaluable for compatibility and may reference a replacement key.
- Retired keys deny access while retaining definitions and history.
- Database and application validation enforce uniqueness and module-prefix consistency.

## 7. Phase 1 data model

### 7.1 Feature definition

Retain `ModuleFeature` as the canonical definition. Add lifecycle/status, optional replacement key and consistent timestamps. A row version may be added now for Phase 2 concurrency.

`IsActive` may remain temporarily for compatibility, but lifecycle becomes authoritative. Migration maps active rows to `active` and inactive rows to `retired`, unless the inventory identifies a safer reviewed mapping.

### 7.2 Tenant feature decision

Add an RLS-protected tenant table, conceptually `tenant_feature_entitlements`, with:

- `Id`, `TenantId`, `FeatureKey`;
- `Decision`: `enabled` or `disabled`;
- `Source`: `migration`, `subscription`, `platform_operator` or `system`;
- bounded non-secret `Reason`/reference;
- `EffectiveFrom` and optional `EffectiveUntil`;
- created/updated actor and timestamp;
- concurrency token.

Constraints:

- feature-key foreign key;
- one current decision per tenant + key;
- RLS filter and write-block predicates;
- no catalog cascade delete;
- check constraints for decision/source values;
- bounded columns and indexes supporting tenant/key evaluation.

Register this table as `RlsPredicate.Tenant`, not `TenantStrict`: tenant mode is restricted to its matching tenant, while explicit admin mode supports audited Platform Admin operations. Cross-tenant background jobs may enumerate tenants in admin mode but must switch into a command-scoped tenant context with `TenantContextSwitcher` before evaluating or changing a tenant decision. Connection context must be restored/cleared on success, failure and rollback.

Phase 1 does not expose general operator CRUD. Compatibility migration and narrowly controlled internal setup are sufficient until Phase 2.

### 7.3 Existing `FeatureAccessGrant`

Do not depend on or extend it during Phase 1. Inventory its data and usage:

- if unused and empty, deprecate and later remove it in a separately reviewed migration;
- if data exists, rename/migrate it according to its actual module-grant meaning;
- never convert role/employee rows automatically into tenant feature decisions.

## 8. Phase 1 effective-decision algorithm

`IFeatureEntitlementService.EvaluateAsync(tenantId, featureKey)` returns:

- `IsEnabled`;
- safe `ReasonCode`;
- feature and owning module keys;
- decision source/configuration version;
- optional effective/expiry time.

Evaluation is fail-closed and ordered:

1. Reject empty or invalid keys.
2. Load the canonical feature and module; reject unknown, retired or inactive rows.
3. Resolve the tenant's latest subscription and evaluate it using `SubscriptionStatusRules.HasActiveAccess`. Active/trial/included statuses are entitled only inside their access window; `past_due` or `cancelled` remain entitled only while `access_ends_at` is in the future. Reject absent, expired or non-entitled subscriptions. Module and feature evaluation must share this rule. Today `ModuleEntitlementService` instead picks the latest subscription whose status is in `SubscriptionStatusRules.ActiveStatuses`, so adopting `HasActiveAccess` there is a deliberate module-level behaviour change (see §11 step 2).
4. Verify the owning module is active in the catalog. If it is one of `PlatformBaselineModules.Keys` (`auth`, `configuration`, `roles`, `notifications`), treat it as always module-entitled and do not require it in `SelectedModulesJson`. Every other module must be selected in the subscription after canonical alias migration.
5. Call the global force-disable hook. Phase 1 provides the evaluation contract but no operator-managed global state; the hook therefore has no active override until Phase 2 supplies the audited state and workflow.
6. Apply a current explicit tenant decision, if present. Explicit enable cannot bypass steps 2–4.
7. Otherwise use `IsDefaultIncluded`.
8. The caller then applies RBAC and resource authorization.

Reason codes include `enabled_default`, `enabled_override`, `invalid_key`, `unknown_feature`, `feature_inactive`, `module_inactive`, `module_not_entitled`, `subscription_inactive`, `disabled_override`, `globally_disabled` and `configuration_error`.

## 9. Phase 1 enforcement surfaces

### HTTP

Add a backend gate such as:

```csharp
[RequireFeature("integrations.api_access")]
```

It resolves the authenticated tenant, calls the central service and returns 401/403 consistently. It never trusts frontend state.

### Background/application work

HTTP attributes do not cover scheduled jobs, outbox consumers, dashboard providers, direct application calls and integration workers. These call the same service before controlled work. Disabled features stop new work safely; retries retain a safe reason code.

### Frontend

The tenant frontend may use a read-only effective-feature summary to hide navigation and present an unavailable state. Direct API calls remain protected. Platform Admin Phase 1 stays read-only but may show the feature's key, module and enforcement status.

## 10. Phase 1 caching and consistency

- Cache only behind the central service.
- Cache key includes tenant ID, feature key and configuration version.
- Ordinary subscription/catalog/tenant-decision changes must propagate within 60 seconds. Negative decisions may be cached.
- A cached decision's absolute expiration is the earlier of the normal TTL and `EffectiveUntil`; an expired row is ignored by the evaluator and may never remain effective from cache.
- Database/cache failure denies controlled operations.
- Subscription/catalog/tenant changes publish invalidation after transaction commit.
- Phase 1 may use per-instance memory caching because the ordinary-change bound is 60 seconds, but the deployment topology and TTL must be explicit and verified for every API instance.
- Before Phase 2 force-disable ships in a multi-instance deployment, add distributed invalidation (shared cache/pub-sub or the approved platform equivalent). Positive decisions also have a five-second TTL safety ceiling while an emergency-control state can exist, so lost invalidation cannot extend access beyond the five-second guarantee.
- Global force-disable bypasses or immediately invalidates positive cache on every instance.
- Metrics record safe result/reason counts, not secrets or personal data.
- Correctness must not depend on cache availability.

## 11. Phase 1 migration and compatibility

1. Inventory and reconcile module identities before feature enforcement. At minimum resolve `org`/`org_structure`, `monitoring`/`activity_monitoring`, and `verification`/`identity_verification` across catalog rows, feature ownership, permissions, subscription plans/snapshots, auto-grants and code constants. Pick one canonical key per module, migrate persisted references, retain an explicit temporary compatibility map only for migration, and do not leave permanent dual identities.
2. Move `ModuleEntitlementService` (`GetActiveModuleKeysForTenantAsync` and its callers) to `SubscriptionStatusRules.HasActiveAccess` in the same release as the feature evaluator, so module and feature decisions cannot disagree. This changes module-level access, not only feature access:
   - `past_due`/`cancelled` tenants inside their grace window (`access_ends_at` in the future) regain module access, permissions and `RequireAnyModule` routes;
   - tenants with an active status whose `access_ends_at` has passed lose module access;
   - role permission assignment (`GetAssignablePermissionsForTenantAsync`) and `DefaultRoleSeeder` follow the same rule.
   Before shipping, list the affected tenants per environment (a read-only query comparing the old and new rule) and record the result in the release notes. Confirm that every trial end sets `access_ends_at`, because `HasActiveAccess` checks `access_ends_at`, not `trial_ends_at`.
3. Inventory seeded feature keys and all endpoints/jobs expected to be controlled.
4. Validate keys and resolve collisions before constraints.
5. Add lifecycle and tenant-decision schema through a forward-only migration.
6. Backfill lifecycle.
7. Preserve current defaults: an active `IsDefaultIncluded=true` feature remains enabled for tenants with an entitled active module unless explicitly disabled. Baseline modules follow the always-entitled rule above.
8. Never auto-enable `IsDefaultIncluded=false` features.
9. Add gates incrementally from the endpoint/job inventory, each with tests.
10. Unknown/corrupt legacy module keys deny access and produce an operational finding.
11. Keep the release additive; do not drop legacy columns in the same deployment.

## 12. Phase 1 security and verification

### Unit and contract tests

- Every decision branch and reason code.
- Canonical/invalid key behaviour.
- Default and explicit decisions.
- Baseline module features remain entitled without appearing in subscription JSON.
- Every legacy module alias pair migrates to one canonical identity without false denial.
- Disabled/retired module and feature denial.
- Tenant override cannot bypass module entitlement.
- `EffectiveUntil` is exclusive: at or after that instant the row is ignored, and cached access expires no later than that instant.
- Every `HasActiveAccess` status and access-end boundary, including `past_due`/`cancelled` immediately before, at and after `access_ends_at`.
- `ModuleEntitlementService` regression: module keys, assignable permissions and `RequireAnyModule` follow `HasActiveAccess` (grace-window `past_due`/`cancelled` entitled; active status past `access_ends_at` denied), and module and feature evaluation agree for the same tenant and instant.
- An ended trial denies access through `access_ends_at`.

### Architecture tests

- Every registered/gated feature key exists in the catalog.
- Every gate key is correctly namespaced.
- Every active customer-facing feature's inventory-listed controllers/jobs have a gate.
- Every metadata-only/internal exclusion has an individually documented reason and recorded security review; there is no broad or implicit exclusion list.
- Tenant decisions are queried only through approved infrastructure.
- Platform mutation commands are audited or narrowly reviewed.

### SQL Server tests

- Tenant A cannot read/write Tenant B decisions through EF or raw SQL.
- Missing/stale `SESSION_CONTEXT` fails closed under connection pooling.
- FK/check/unique constraints reject invalid rows.
- Runtime identity cannot mutate catalog definitions through ad-hoc SQL.

### UAT tests

- Default-included feature works for an entitled tenant.
- Disabled feature is hidden and direct API access is rejected.
- Tenant without module remains denied despite an attempted explicit enable.
- Suspended subscription removes access.
- Invalidation changes effective access inside the approved window.
- A baseline feature such as `auth.mfa_enforcement` remains available without `auth` in `SelectedModulesJson`.
- Alias-migrated tenants do not lose access.

## 13. Phase 1 acceptance criteria

- One central service owns all effective feature decisions.
- All active customer-facing feature HTTP and non-HTTP surfaces use it and fail closed.
- Module/catalog/subscription validation is enforced server-side.
- Tenant decisions have FK, constraints and RLS coverage.
- Migration preserves approved default behaviour.
- Baseline modules, canonical alias migration and DEC-037 status semantics are covered by regression tests.
- `ModuleEntitlementService` uses the same `HasActiveAccess` rule, and the affected-tenant report for that change is recorded.
- Unit, architecture, SQL Server isolation and UAT tests pass.
- No feature entitlement relies only on frontend checks.

## 14. Phase 2 Platform Admin management

### Catalog workflow

Authorized operators can create a draft, edit safe metadata, activate, deprecate and retire through allowed transitions, and view defaults/replacement/enforcement coverage. They cannot rename, reuse or delete an issued key.

Writes require AdminPolicy/MFA, CSRF, least-privilege permission, reason capture, optimistic concurrency and privileged audit coverage.

### Tenant override workflow

The tenant detail page shows effective features grouped by module, including default, explicit decision, source, effective period and final state. An operator may enable/disable only inside an active subscribed module and may restore default through a separate audited command. Every write revalidates subscription and catalog state.

Phase 2 supports an immediate decision with optional expiry. Future scheduling and queued next decisions are deferred; `EffectiveFrom` is the command time in this phase.

Platform Admin uses the existing explicitly audited admin-mode RLS path to address the selected tenant. Each command resolves the target tenant server-side, scopes the transaction to that target, and must not rely on caller-supplied session context. Pooled-connection cleanup and cross-tenant write tests are mandatory.

### Emergency kill switch

- A global operation targets one feature key across all tenants. It is **force-disable only**; it cannot disable every product feature at once and cannot globally enable a paid/restricted feature.
- It requires the strongest permission, explicit confirmation, reason and incident/support reference.
- The preview shows affected tenants/count without exposing data beyond permission.
- Audit is written with the state change; positive caches invalidate immediately.
- Distributed invalidation reaches every API instance, with the five-second positive-TTL ceiling as a backstop.
- Restore is a separate command and never removes history.
- UI distinguishes operational disable, retirement and tenant override.

### Versioning, history and rollback

- Definitions and decisions carry row version/concurrency tokens.
- Append-only history stores safe before/after state, actor, action, reason, correlation/trace reference and time.
- History is server-paged and filterable by key, tenant, actor, action and date.
- Rollback creates a new validated version; history is never rewritten.
- Retention follows DEC-002: one year online then archive; no Release 1 purge.

## 15. Phase 2 operator flow

```text
Module Catalog → Feature detail
    ├─ definition and lifecycle
    ├─ enforcement coverage
    ├─ tenant decisions
    └─ version / audit history

Tenant detail → Features
    ├─ effective state and source
    ├─ subscription/module boundary
    ├─ enable / disable / restore default
    └─ expiry, reason and history

Operations → Emergency controls
    └─ force-disable → confirm → audit → invalidate → verify
```

## 16. Phase 2 acceptance criteria

- Approved lifecycle and tenant decisions require no direct database editing.
- All writes enforce MFA, permission, CSRF, validation and concurrency.
- Successful and meaningful failed mutations create safe audit records.
- Force-disable/restore propagates within 5 seconds; stale cache cannot grant access.
- Restore/rollback creates a new audited version.
- UI communicates effective state/source/reason without secrets.
- Playwright covers lifecycle, override, conflict, emergency disable and direct-API denial.

## 17. Errors and observability

- Tenant errors expose a stable problem type and correlation reference, not internal configuration.
- Authorized operator screens may show safe reason codes and versions.
- Metrics include decision totals, latency, cache hit/miss, invalidation lag and configuration failures.
- Logs/traces contain approved identifiers, correlation/trace IDs and reason codes, never credentials or personal data.
- Alert on unknown production keys, repeated evaluator failures, invalidation backlog and stale emergency disables.

## 18. Delivery order

### Phase 1

1. Catalog/key and endpoint/job inventory, module alias reconciliation, and the affected-tenant report for the subscription-rule change.
2. Additive schema, constraints, lifecycle migration and RLS.
3. `ModuleEntitlementService` on `HasActiveAccess`, then the central evaluator and cache/invalidation contract (shipped together).
4. HTTP gate and non-HTTP integration.
5. Incremental endpoint/job coverage.
6. Unit, architecture, SQL Server and Playwright/UAT evidence.

### Phase 2

**Dependency:** the F-07 platform audited-command pipeline must be merged into `origin/development`, its audit migrations/grants applied, and its architecture tests green before Phase 2 mutation work begins. It exists in the local development history reviewed on 2026-11-16 but is not yet an ancestor of `origin/development`.

1. Catalog lifecycle commands/API.
2. Tenant decision commands/API.
3. Global force-disable and cache verification.
4. Version/history/restore APIs.
5. Platform Admin screens and operator runbook.
6. Full audit, concurrency and Playwright evidence.

Phase 2 depends on Phase 1's central evaluator; no UI implements its own decision rules.

## 19. Builder-owned details

The implementation plan may select exact class, table, endpoint and cache names while preserving these boundaries. Before estimating batches, it must include the feature-controlled endpoint/job inventory. Any change to default semantics, module boundary, immutable keys, force-disable-only behaviour or audit requirements returns for a product/security decision.

## 20. Approval gate

No product implementation starts from this document until Dabi approves it. After approval, create a separate implementation plan with atomic tasks, repository ownership, migration order, test commands and PR boundaries.
