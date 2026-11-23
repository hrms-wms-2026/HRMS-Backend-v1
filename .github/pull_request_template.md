## Summary

<!-- What changed and why. -->

## Checklist

- [ ] New or changed response DTO: no secret, no internal id, restricted personal data is behind a permission. See `docs/security/data-classification.md`.
- [ ] New tenant-owned table has an `RlsTableRegistry` entry and a migration that applies its policy.
- [ ] New endpoint has `[RequirePermission]` (or `[AllowAnonymous]` with its own authentication).
