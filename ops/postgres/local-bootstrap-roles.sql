-- Local/dev/test PostgreSQL role bootstrap helper.
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

-- Column-level grants only: exactly the columns auth_lookup_base_login_candidates
-- references, not every column on users/tenants. Revoke first so re-running this script
-- against a database that still has an old broad table-level grant repairs it.
SELECT format('REVOKE SELECT ON users, tenants FROM %I', :'base_login_fn_owner')
\gexec

SELECT format(
    'GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash) ON public.users TO %I',
    :'base_login_fn_owner')
\gexec

SELECT format(
    'GRANT SELECT (id, slug, name, status) ON public.tenants TO %I',
    :'base_login_fn_owner')
\gexec

SELECT format('GRANT %I TO %I', :'base_login_fn_owner', :'migrator_user')
\gexec
