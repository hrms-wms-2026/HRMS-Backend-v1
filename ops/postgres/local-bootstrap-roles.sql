-- Local/dev/test PostgreSQL PRE-MIGRATION role bootstrap helper.
--
-- This operational helper creates equivalent local roles for the
-- architecture-defined runtime/migration role model. The API must never run
-- this file. Production and staging must provision equivalent roles through
-- their hosting/database/deployment process.
--
-- User names and passwords are required psql variables supplied by
-- ops/postgres/setup-local-db.ps1 from the local, gitignored .env file. Never
-- store real passwords in this file or print them from setup tooling.
--
-- The runtime role is restricted, NOSUPERUSER, and NOBYPASSRLS. The migration
-- role is used only for schema migration and is also not a superuser.
--
-- This file runs BEFORE EF migrations, so it must only do things that are
-- valid against an empty/pre-schema database: it must never reference the
-- users/tenants tables (schema public), or any other migrated table/
-- function, by name. Object grants that require those migrated objects to
-- exist live in ops/postgres/local-post-migration-grants.sql instead, which
-- runs after migrations succeed.

\set ON_ERROR_STOP on

SELECT format('CREATE ROLE %I', :'migrator_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'migrator_user')
\gexec

SELECT format('CREATE ROLE %I', :'app_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec

-- Always re-apply login restrictions and the password. Re-running setup
-- therefore repairs an existing role that still has an old local password.
SELECT format(
    'ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION',
    :'migrator_user', :'migrator_password')
\gexec

SELECT format(
    'ALTER ROLE %I WITH LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION',
    :'app_user', :'app_password')
\gexec

SELECT format('GRANT CREATE, USAGE ON SCHEMA public TO %I', :'migrator_user')
\gexec

-- Database-level CREATE only, and only for onevo_migrator: some migrations create new
-- schemas (e.g. CREATE SCHEMA IF NOT EXISTS auth_internal), which requires CREATE on the
-- database itself, not just on the public schema. onevo_app must never receive this -
-- runtime must not be able to create schemas or tables.
SELECT format('GRANT CREATE ON DATABASE %I TO %I', :'db_name', :'migrator_user')
\gexec

SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'app_user')
\gexec

SELECT format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
    :'migrator_user', :'app_user')
\gexec

SELECT format(
    'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I',
    :'migrator_user', :'app_user')
\gexec

SELECT format(
    'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO %I',
    :'app_user')
\gexec

SELECT format(
    'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO %I',
    :'app_user')
\gexec

-- Dedicated NOLOGIN, BYPASSRLS owner for the sole pre-tenant base-login lookup function.
-- This role can never open a database connection itself (NOLOGIN); it exists only so the
-- function it owns can run SECURITY DEFINER with BYPASSRLS privileges regardless of the
-- calling session's RLS context. onevo_migrator is granted membership in this role so the
-- migration (which runs as onevo_migrator) can legally ALTER FUNCTION ... OWNER TO it.
SELECT format('CREATE ROLE %I', :'base_login_fn_owner')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'base_login_fn_owner')
\gexec

SELECT format('ALTER ROLE %I WITH NOLOGIN BYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION',
    :'base_login_fn_owner')
\gexec

SELECT format('GRANT USAGE ON SCHEMA public TO %I', :'base_login_fn_owner')
\gexec

-- users/tenants column-level grants for base_login_fn_owner cannot run here: on a fresh
-- database those tables (schema public) do not exist yet until EF migrations create them.
-- See ops/postgres/local-post-migration-grants.sql, which runs after migrations and holds
-- those grants instead.

SELECT format('GRANT %I TO %I', :'base_login_fn_owner', :'migrator_user')
\gexec
