# Notification templates & channel mapping — implementation decisions

Plan: `C:/HR2/docs/superpowers/plans/2026-10-16-notification-templates-and-channel-mapping.md`.
The product owner asked implementation to proceed with sensible defaults for the plan's open
questions (2026-10-18). These are the defaults chosen; each is reversible before release.

| # | Question | Decision |
|---|---|---|
| 1 | Product defaults approval | Accepted as written in the plan: existing channels keep their current defaults, newly supported channels start OFF, no tenant-specific copy / personal preferences / quiet hours in v1. |
| 2 | First-slice event | `work_approval_requested` (WorkNotificationEngine → `work_notification` outbox). Already outbox-backed and resolves the recipient's UserId. |
| 3 | Tenant permission | `notifications:manage` (seeded in PermissionSeeder + ModuleCatalogSeeder). |
| 4 | Sanitizer / CSS inliner | No new package. Email HTML is **validated and rejected**, not sanitized: an allow-list of tags/attributes, no scripts, no event handlers, no `javascript:`/`data:` URLs, no `@import`/external stylesheets. A small in-house inliner applies simple element / `.class` / `#id` rules from the CSS field to inline `style` attributes; unsupported selectors are a validation error. |
| 5 | Tray capability negotiation | Additive query parameter `capabilities=generic` on the existing pending endpoint. Absent → legacy monitoring alerts only. |
| 6 | Cutover flag storage | Code-level per-event ownership in the catalog (`OrchestratorOwned`), toggled per event in a deploy. No runtime DB flag (avoids both owners being on at once). |
| 7 | Action key → navigation | `open_related` reuses the existing in-app navigation (`RelatedEntityType`/`RelatedEntityId` → `InboxNotificationNavigation`). `none` = no click action. |
| 8 | Platform-wide (non-tenant) emails | Out of tenant routing in v1. Invites, password reset/changed and invoices stay on `EmailTemplateRenderer` and are listed as excluded in the inventory. |
| 9 | Numbers | Test send: 10 per admin per hour. Tray delivery expiry: 24 h. Delivery records: retained with the notification; cleanup job is a follow-up. |
| 10 | In-app storage | The existing `notifications` table remains the inbox. The in-app delivery handler inserts that row; `notification_deliveries.notification_id` references it. |

Other conventions taken from the codebase (override the plan text where they differ):

- Admin API routes are `admin/v1/notification-templates` and `admin/v1/notification-events`
  with `AdminPolicy` + `PlatformPermissionCatalog.TemplatesRead` / `TemplatesManage`.
- Tenant API is `api/v1/settings/notifications` guarded by `notifications:manage`.
- Stable event identity: a `Guid EventId` is generated at enqueue time inside the business
  transaction and persisted in the outbox payload, so retries reuse it. Old payloads without
  it fall back to the legacy path.
- Recipient rules: v1 registers one rule per event (`default` = exactly what the producer resolves
  today). Additional rules are added per event only when the producer can resolve them safely.
