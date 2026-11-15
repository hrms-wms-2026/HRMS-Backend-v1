# Task 09: Deploy pipeline and database migration step

| | |
|---|---|
| Priority | 9 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Deployment, DB |
| Depends on | 01, 05 |

## What we found

- `ci.yml` builds and tests only. Nothing publishes an image or deploys.
- `Program.cs` runs migrations automatically only in Development, which is correct. But there is no documented or automated way to apply migrations to UAT.
- `ops/sqlserver` holds `bootstrap-logins.sql` and `post-migration-grants.sql`, and `.env.example` defines three database users (admin, app, migrator). The pieces exist but they are run by hand.
- Unlike PostgreSQL, SQL Server row-level security applies to every login, so a migration run with the wrong context can fail or silently skip rows.

## What to do

1. Add a deploy workflow: build the image from task 01, push it to the registry, deploy to UAT on a manual approval.
2. Add a migration job that runs before the new version takes traffic: build an EF migrations bundle in CI, run it with the migrator login, then run `post-migration-grants.sql`.
3. The app login must not own the schema and must not be able to change RLS policies. Verify the three logins have only the rights they need.
4. Take a database backup before each migration in UAT and write the restore steps.
5. Write `docs/runbooks/uat-deploy.md`: deploy, roll back, re-run a failed migration.
6. Make the image tag and the git commit visible in a `/health` field or a response header.

## Done when

- One approved workflow run takes a commit to UAT, including migrations.
- Roll back to the previous image is tested once.
- The runbook exists and a second person has followed it successfully.
