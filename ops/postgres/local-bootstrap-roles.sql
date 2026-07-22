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
