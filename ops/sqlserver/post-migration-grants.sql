-- Privileged step, run after migrations. onevo_app gets data access and one procedure, nothing
-- else: never db_owner, never ALTER ANY SECURITY POLICY, never SELECT on schema rls.
USE [$(DB_NAME)];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO onevo_app;
GRANT EXECUTE ON auth_internal.auth_lookup_base_login_candidates TO onevo_app;
GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash)
    ON dbo.users TO onevo_auth_base_login_fn_owner;
GRANT SELECT (id, slug, name, status) ON dbo.tenants TO onevo_auth_base_login_fn_owner;
GO
