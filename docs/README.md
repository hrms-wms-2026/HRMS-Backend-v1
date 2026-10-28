# Backend docs

| Folder | What's in it |
|---|---|
| [development/](development/) | Local dev setup — [ports.md](development/ports.md) (also mirrored in the platform-admin front-end repo). |
| [deployment/](deployment/) | Deployment and secret-handling notes. |
| [notifications/](notifications/) | Notification event inventory, decisions and rollout. |
| [postman-request/](postman-request/) | API request docs by area, plus the `ONEVO-HRMS.postman_collection.json` export. |
| [superpowers/](superpowers/) | Plans (`plans/next`, `plans/finished`), specs, rules and workflow notes. |
| [reports/](reports/) | Implementation / verification / fix reports, grouped by feature area. |

Database schema snapshots live in [`../schema-snapshots/`](../schema-snapshots/README.md) at the
repo root, because the generator resolves `.env` relative to that folder.

Postgres ops notes: [`../ops/postgres/README.md`](../ops/postgres/README.md).

## Conventions

- File names: lowercase kebab-case. Dated docs start with `YYYY-MM-DD-`.
- Put new reports in `reports/<area>/`, not at the repo root.
- Move files with `git mv` so history follows them.
