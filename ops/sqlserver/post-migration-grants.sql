-- Privileged step, run after migrations. crownus_app gets data access and one procedure, nothing
-- else: never db_owner, never ALTER ANY SECURITY POLICY, never SELECT on schema rls.
USE [$(DB_NAME)];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO crownus_app;
GRANT EXECUTE ON auth_internal.auth_lookup_base_login_candidates TO crownus_app;
GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash)
    ON dbo.users TO crownus_auth_base_login_fn_owner;
GRANT SELECT (id, slug, name, status) ON dbo.tenants TO crownus_auth_base_login_fn_owner;
GO
