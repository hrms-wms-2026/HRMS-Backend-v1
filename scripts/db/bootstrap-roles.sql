-- Dev/test bootstrap: separates the schema-owning migration role from the
-- restricted runtime application role so PostgreSQL Row-Level Security is
-- actually enforced for the app connection. PostgreSQL superusers and roles
-- with the BYPASSRLS attribute silently skip RLS regardless of
-- FORCE ROW LEVEL SECURITY, which is why the runtime app must never connect
-- as postgres/a superuser.
--
-- Run once, connected as an existing PostgreSQL superuser (e.g. `postgres`),
-- against the target database:
--   psql -h localhost -U postgres -d OnevoDb -f scripts/db/bootstrap-roles.sql
--
-- Idempotent and safe to re-run. Run it BEFORE the first
-- `dotnet ef database update`, so onevo_migrator (not postgres) becomes the
-- owner of every table the migrations create -- table ownership is what lets
-- a non-superuser role run ALTER TABLE ... FORCE ROW LEVEL SECURITY /
-- CREATE POLICY without needing superuser itself.
--
-- The passwords below are local-dev placeholders matching
-- appsettings.Development.json. Set real, unique passwords via \set or
-- environment substitution before running this against any shared/CI
-- database; never reuse these values in production.

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'onevo_migrator') THEN
        CREATE ROLE onevo_migrator LOGIN PASSWORD 'onevo_migrator_dev_only' NOSUPERUSER NOBYPASSRLS;
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'onevo_app') THEN
        CREATE ROLE onevo_app LOGIN PASSWORD 'onevo_app_dev_only' NOSUPERUSER NOBYPASSRLS;
    END IF;
END
$$;

-- onevo_migrator needs to be able to create tables (it then owns whatever it
-- creates, which is what lets it run FORCE ROW LEVEL SECURITY / CREATE POLICY
-- as a non-superuser).
GRANT CREATE, USAGE ON SCHEMA public TO onevo_migrator;

-- onevo_app is the runtime application role: normal DML only, no DDL, no
-- superuser/BYPASSRLS.
GRANT USAGE ON SCHEMA public TO onevo_app;

-- Every table onevo_migrator creates from now on (future migrations included)
-- is automatically readable/writable by onevo_app -- no manual GRANT needed
-- per migration.
ALTER DEFAULT PRIVILEGES FOR ROLE onevo_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO onevo_app;

-- Covers tables that already exist before this script runs (idempotent; a
-- fresh database has none yet).
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app;
