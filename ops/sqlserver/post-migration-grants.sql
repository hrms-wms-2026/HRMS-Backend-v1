-- Privileged step, run after migrations. crownus_app gets data access and one procedure, nothing
-- else: never db_owner, never ALTER ANY SECURITY POLICY, never SELECT on schema rls.
USE [$(DB_NAME)];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO crownus_app;
GRANT EXECUTE ON auth_internal.auth_lookup_base_login_candidates TO crownus_app;
-- Audit tables are append-only (security findings F-07, PA-SEC-002): the app may insert and read audit
-- rows, never change or remove them. DENY overrides the schema-wide GRANT above. Their foreign keys are
-- NO ACTION, because a cascading SET NULL would rewrite audit rows without a permission check.
DENY UPDATE, DELETE ON dbo.platform_audit_logs TO crownus_app;
DENY UPDATE, DELETE ON dbo.audit_logs TO crownus_app;
DENY UPDATE, DELETE ON dbo.platform_auth_events TO crownus_app;
DENY UPDATE, DELETE ON dbo.billing_audit_logs TO crownus_app;
GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash)
    ON dbo.users TO crownus_auth_base_login_fn_owner;
GRANT SELECT (id, slug, name, status) ON dbo.tenants TO crownus_auth_base_login_fn_owner;
GO
