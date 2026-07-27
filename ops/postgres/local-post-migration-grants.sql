-- Local/dev/test PostgreSQL POST-MIGRATION object grants helper.
--
-- This operational helper applies object-level grants that require
-- public.users, public.tenants, and auth_internal.auth_lookup_base_login_candidates
-- to already exist. The API must never run this file. Production and staging
-- must provision equivalent grants through their hosting/database/deployment
-- process.
--
-- This file runs ONLY after EF migrations have created the schema. It must
-- never be run against an empty/pre-schema database - see
-- ops/postgres/local-bootstrap-roles.sql for the pre-migration role bootstrap
-- step that runs before migrations.
--
-- The base_login_fn_owner name is a required psql variable supplied by
-- ops/postgres/setup-local-db.ps1, matching the value passed to
-- local-bootstrap-roles.sql.

\set ON_ERROR_STOP on

-- Column-level grants only: exactly the columns auth_lookup_base_login_candidates
-- references, not every column on users/tenants. Revoke first so re-running this script
-- against a database that still has an old broad table-level grant repairs it.
SELECT format('REVOKE SELECT ON public.users, public.tenants FROM %I', :'base_login_fn_owner')
\gexec

SELECT format(
    'GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash) ON public.users TO %I',
    :'base_login_fn_owner')
\gexec

SELECT format(
    'GRANT SELECT (id, slug, name, status) ON public.tenants TO %I',
    :'base_login_fn_owner')
\gexec
