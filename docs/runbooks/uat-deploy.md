# UAT deploy runbook

How to deploy Crownus to the UAT App Service, roll it back, and recover a failed migration. Task 09
(`docs/Improvements/person-4-deployment-and-architecture/task-09 Deploy pipeline and database migration step.md`).

UAT today is the Azure App Service from `ops/appservice/`: one zip with the API, web app and admin console,
deployed by `.github/workflows/deploy-appservice.yml`. The database is `CrownusDB_test` on the SQL Server
instance on the VM (`9.223.115.62,51433`). That instance belongs to the server owner, so only database-scoped
actions on `CrownusDB_test` are allowed: no instance settings, no restarts, nothing on other databases.

One-time setup (secrets, variables, portal switches): [deploy-pipeline-setup.md](../../ops/appservice/deploy-pipeline-setup.md).
Settings the API needs: [uat-configuration.md](../deployment/uat-configuration.md).

## Who runs what

| Step | Who | Login |
|---|---|---|
| Deploy, roll back | anyone with Run rights on the Actions tab (plus a reviewer if `appservice-test` requires one) | GitHub |
| Migrations | the workflow, when **apply migrations** is ticked | `crownus_migrator`, from the `MIGRATION_CONNECTION_STRING` secret |
| Backup, grants, rights check, restore | a person with the SQL admin login | admin login, from SSMS or `sqlcmd` on a machine that can reach the server |

Never paste a password into a command that is saved in shell history, a ticket or chat. `sqlcmd` asks for the
password when `-P` is left out.

## Before you start

1. Nothing else is deploying: Actions → *Deploy to App Service* shows no run in progress. A later run can drop a
   waiting manual run.
2. Note what is live now, so you can roll back to it. Open the last successful run of *Deploy to App Service*;
   its summary lists the **backend**, **web** and **admin** commits. Save them in the release ticket.
3. Check whether the release has a new migration: compare `src/ONEVO.Infrastructure/Migrations/` between the
   live backend commit and the new one.

   ```powershell
   git diff --stat <live-backend-sha> <new-backend-sha> -- src/ONEVO.Infrastructure/Migrations
   ```

   No `.cs` files listed = no migration. Skip the backup, migration and grants steps.

## Deploy without a migration

1. Actions → *Deploy to App Service* → **Run workflow**.
2. Set `backend_ref`, `web_ref` and `admin_ref` to the commits or tags to release. Leave **apply migrations**
   unticked.
3. Approve the `appservice-test` environment if asked.
4. The run passes only when the site serves this run's build id at `https://<host>/build-id.txt` and
   `/health/ready` returns `200`, within 10 minutes.
5. Smoke check by hand: sign in to the web app and the admin console, open the inbox and one employee.
6. Write the three commits and the run link in the release ticket.

## Deploy with a migration

### 1. Back up the database

Copy-only, so it does not disturb the owner's backup chain. Run as the admin login:

```powershell
sqlcmd -S 9.223.115.62,51433 -U <admin login> -C -b -Q "DECLARE @file nvarchar(400) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260)) + N'\CrownusDB_test_predeploy_' + FORMAT(SYSUTCDATETIME(), 'yyyyMMdd_HHmm') + N'.bak'; BACKUP DATABASE [CrownusDB_test] TO DISK = @file WITH COPY_ONLY, CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM; SELECT @file AS backup_file;"
```

It must end with `The backup set on file 1 is valid.` Write the `backup_file` path in the release ticket. The
file is on the VM's disk in the instance's default backup folder; delete old `*_predeploy_*` files after a few
releases. (Tested 2026-11-19 on the local SQL Server container, where the path uses `/`.)

### 2. Run the deploy with migrations

1. Actions → *Deploy to App Service* → **Run workflow**, same refs as above, **apply migrations** ticked.
2. The deploy job builds an EF migration bundle from the same backend commit and runs it as `crownus_migrator`
   **before** it deploys the new zip. Migrations must therefore keep working with the old code for those few
   minutes: add columns and tables, don't rename or drop in the same release.
3. If the migration step fails, the new zip is not deployed. Go to [A migration failed](#a-migration-failed).

### 3. Grants and rights check

`crownus_migrator` cannot grant permissions. After every migration, run as the admin login:

```powershell
sqlcmd -S 9.223.115.62,51433 -U <admin login> -C -b -i ops/sqlserver/post-migration-grants.sql -v DB_NAME=CrownusDB_test
sqlcmd -S 9.223.115.62,51433 -U <admin login> -C -b -i ops/sqlserver/check-login-rights.sql -v DB_NAME=CrownusDB_test
```

The second command must print `Login rights OK`. It checks that `crownus_app` has data access only (no role, no
schema or object ownership, no `ALTER ANY SECURITY POLICY`, nothing on schema `rls`) and that neither login is
a server admin or the database owner. Any other right is listed and the command exits with code 1.

### 4. Check and record

As in [Deploy without a migration](#deploy-without-a-migration), steps 4 to 6. Add the backup file name.

## Roll back the app

Roll back by deploying the previous commits again: the workflow rebuilds that zip, so the result is the same as
the earlier deploy.

1. Actions → *Deploy to App Service* → **Run workflow** with the **previous** backend, web and admin commits
   from the release ticket. Leave **apply migrations** unticked.
2. Wait for the run to pass (new build id served, `/health/ready` = `200`).

This is enough when the release had no migration, or when its migration only added things the old code ignores
(the normal case, see step 2 above).

## Roll back the database

Only when the old code cannot run on the new schema, or the migration damaged data. Restoring loses every change
made in UAT since the backup.

1. Stop the App Service (portal → Overview → **Stop**), so nothing writes during the restore.
2. Restore the backup over `CrownusDB_test`, as the admin login:

   ```powershell
   sqlcmd -S 9.223.115.62,51433 -U <admin login> -C -b -Q "ALTER DATABASE [CrownusDB_test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [CrownusDB_test] FROM DISK = N'<backup_file>' WITH REPLACE, CHECKSUM; ALTER DATABASE [CrownusDB_test] SET MULTI_USER;"
   ```

   If the restore fails, the database stays in single-user mode: fix the cause and run the command again, or run
   `ALTER DATABASE [CrownusDB_test] SET MULTI_USER;` before starting the app.
   The logins are on the same instance, so the restored users still map to them. Run
   `check-login-rights.sql` again; it must print `Login rights OK`.
3. Roll back the app as above, to the commits that match the restored schema.
4. Start the App Service and check `/health/ready`.

Do not use the migration bundle's "migrate down" to a target migration on UAT. Down migrations are not tested and
can drop data.

## A migration failed

EF applies migrations inside a transaction (none of the migrations in this repo turns that off), so the failed
migration leaves no half-applied changes. Check the history table below to see which earlier ones are applied.

1. Read the error in the failed run's **Apply migrations** step.
2. See what is applied, as any login that can read the database:

   ```sql
   SELECT TOP (5) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;
   ```

3. Cause outside the code (server unreachable, timeout, wrong secret): fix it and run the workflow again with
   **apply migrations** ticked. The bundle only applies what is still pending.
4. Cause in the migration itself (bad SQL, data that breaks a new constraint): fix it in a new backend commit,
   test it against a copy of the data, then run the workflow with that commit.
5. The app was not deployed, so the old version keeps running. If the applied migrations already broke it, roll
   back the database.

The API does not check for pending migrations when it starts outside Development. A missing migration shows up
as SQL errors on the first requests that touch the new columns, not as a failed start.

## Find out what is running

| Question | Where |
|---|---|
| Which build is live? | `https://<host>/build-id.txt` = the run id and attempt of the deploy that put it there |
| Which commits? | that run's summary on the Actions tab |
| Is it healthy? | `https://<host>/health` (process) and `https://<host>/health/ready` (database) |
| Latest migration applied? | the `__EFMigrationsHistory` query above |

## Not done yet

- **Never run end to end.** The organisation's Actions minutes ran out in November 2026, so neither a deploy, a
  rollback nor a migration has gone through the workflow. The first run must follow this runbook step by step,
  and a second person should repeat it once.
- **The backup is a manual step.** Automating it would need a SQL login with backup rights on the owner's
  instance in a GitHub secret. Decide that with the server owner.
- **Container deploys.** The image from [container.md](../deployment/container.md) is not deployed anywhere. The
  pipeline stays a zip deploy until UAT has a container host.
