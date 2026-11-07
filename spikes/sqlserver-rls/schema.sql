-- Throwaway spike schema. Proves spec section 4. Run as sa, split on GO.
CREATE DATABASE crownus_spike COLLATE Latin1_General_100_CS_AS_SC;
GO
ALTER DATABASE crownus_spike SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
USE crownus_spike;
GO
ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1;
GO
CREATE LOGIN onevo_app WITH PASSWORD = 'Spike_App_Pw_12345!', CHECK_POLICY = OFF;
CREATE LOGIN onevo_migrator WITH PASSWORD = 'Spike_Mig_Pw_12345!', CHECK_POLICY = OFF;
CREATE USER onevo_app FOR LOGIN onevo_app;
CREATE USER onevo_migrator FOR LOGIN onevo_migrator;
CREATE USER onevo_auth_base_login_fn_owner WITHOUT LOGIN;
GO
CREATE TABLE tenants (
    id uniqueidentifier NOT NULL PRIMARY KEY,
    slug nvarchar(100) NOT NULL,
    name nvarchar(200) NOT NULL,
    status nvarchar(20) NOT NULL);
CREATE TABLE users (
    id uniqueidentifier NOT NULL PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    normalized_email nvarchar(320) NOT NULL,
    password_hash nvarchar(255) NOT NULL,
    is_active bit NOT NULL,
    is_deleted bit NOT NULL,
    private_note nvarchar(100) NULL);
CREATE INDEX ix_users_email ON users (normalized_email);
CREATE TABLE sessions (
    id uniqueidentifier NOT NULL PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    key_hash nvarchar(128) NOT NULL);
CREATE INDEX ix_sessions_key_hash ON sessions (key_hash);
CREATE TABLE items (
    id bigint IDENTITY PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    payload nvarchar(100) NOT NULL,
    amount int NOT NULL);
CREATE TABLE tray_codes (
    id bigint IDENTITY PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    code nvarchar(20) NOT NULL);
CREATE TABLE files (
    id bigint IDENTITY PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(100) NOT NULL);
CREATE TABLE tracked_items (
    id bigint IDENTITY PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    name nvarchar(100) NOT NULL);
CREATE TABLE tracked_log (
    id bigint IDENTITY PRIMARY KEY,
    tenant_id uniqueidentifier NOT NULL,
    item_id bigint NOT NULL);
GO
CREATE TRIGGER trg_tracked_items_log ON tracked_items AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO tracked_log (tenant_id, item_id) SELECT tenant_id, id FROM inserted;
END;
GO
CREATE SCHEMA rls;
GO
CREATE SCHEMA auth_internal;
GO
-- Standard: admin, or tenant with a matching id. Missing or unreadable context matches nothing.
CREATE FUNCTION rls.fn_tenant(@tenant_id uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS RETURN
    SELECT 1 AS allowed
    WHERE CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'admin'
       OR (CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'tenant'
           AND @tenant_id = TRY_CONVERT(uniqueidentifier,
               CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36))));
GO
CREATE FUNCTION rls.fn_tenant_or_system(@tenant_id uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS RETURN
    SELECT 1 AS allowed
    WHERE CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) IN (N'admin', N'system')
       OR (CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'tenant'
           AND @tenant_id = TRY_CONVERT(uniqueidentifier,
               CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36))));
GO
-- Matches the three Postgres tables with no admin exception.
CREATE FUNCTION rls.fn_tenant_strict(@tenant_id uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS RETURN
    SELECT 1 AS allowed
    WHERE @tenant_id = TRY_CONVERT(uniqueidentifier,
          CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36)));
GO
-- Sessions read filter: the standard rule, or the key being looked up.
CREATE FUNCTION rls.fn_sessions_read(@tenant_id uniqueidentifier, @key_hash nvarchar(128))
RETURNS TABLE WITH SCHEMABINDING AS RETURN
    SELECT 1 AS allowed
    WHERE CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'admin'
       OR (CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'tenant'
           AND @tenant_id = TRY_CONVERT(uniqueidentifier,
               CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36))))
       OR @key_hash = CAST(SESSION_CONTEXT(N'app.session_lookup_key_hash') AS nvarchar(128));
GO
-- Users read filter: the standard rule, or the login lookup procedure's dedicated user.
CREATE FUNCTION rls.fn_users_read(@tenant_id uniqueidentifier)
RETURNS TABLE WITH SCHEMABINDING AS RETURN
    SELECT 1 AS allowed
    WHERE CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'admin'
       OR (CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16)) = N'tenant'
           AND @tenant_id = TRY_CONVERT(uniqueidentifier,
               CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36))))
       OR USER_NAME() = N'onevo_auth_base_login_fn_owner';
GO
CREATE SECURITY POLICY rls.policy_items
    ADD FILTER PREDICATE rls.fn_tenant(tenant_id) ON dbo.items,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.items AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.items AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.items BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.items BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_tray_codes
    ADD FILTER PREDICATE rls.fn_tenant_or_system(tenant_id) ON dbo.tray_codes,
    ADD BLOCK PREDICATE rls.fn_tenant_or_system(tenant_id) ON dbo.tray_codes AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant_or_system(tenant_id) ON dbo.tray_codes AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant_or_system(tenant_id) ON dbo.tray_codes BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant_or_system(tenant_id) ON dbo.tray_codes BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_files
    ADD FILTER PREDICATE rls.fn_tenant_strict(tenant_id) ON dbo.files,
    ADD BLOCK PREDICATE rls.fn_tenant_strict(tenant_id) ON dbo.files AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant_strict(tenant_id) ON dbo.files AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant_strict(tenant_id) ON dbo.files BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant_strict(tenant_id) ON dbo.files BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_sessions
    ADD FILTER PREDICATE rls.fn_sessions_read(tenant_id, key_hash) ON dbo.sessions,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.sessions AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.sessions AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.sessions BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.sessions BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_users
    ADD FILTER PREDICATE rls.fn_users_read(tenant_id) ON dbo.users,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.users AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.users AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.users BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.users BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_tracked_items
    ADD FILTER PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_items,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_items AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_items AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_items BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_items BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
CREATE SECURITY POLICY rls.policy_tracked_log
    ADD FILTER PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_log,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_log AFTER INSERT,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_log AFTER UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_log BEFORE UPDATE,
    ADD BLOCK PREDICATE rls.fn_tenant(tenant_id) ON dbo.tracked_log BEFORE DELETE
    WITH (STATE = ON, SCHEMABINDING = ON);
GO
CREATE PROCEDURE auth_internal.auth_lookup_base_login_candidates
    @normalized_email nvarchar(320)
WITH EXECUTE AS N'onevo_auth_base_login_fn_owner'
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (9) u.tenant_id, u.id AS user_id, t.slug, t.name AS display_name, u.password_hash
    FROM dbo.users u
    JOIN dbo.tenants t ON t.id = u.tenant_id
    WHERE u.normalized_email = @normalized_email
      AND u.is_active = 1
      AND u.is_deleted = 0
      AND t.status IN (N'Active', N'Trial')
    ORDER BY u.tenant_id, u.id;
END;
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO onevo_app;
GRANT EXECUTE ON auth_internal.auth_lookup_base_login_candidates TO onevo_app;
GRANT SELECT (tenant_id, id, normalized_email, is_active, is_deleted, password_hash)
    ON dbo.users TO onevo_auth_base_login_fn_owner;
GRANT SELECT (id, slug, name, status) ON dbo.tenants TO onevo_auth_base_login_fn_owner;
ALTER ROLE db_ddladmin ADD MEMBER onevo_migrator;
GRANT ALTER ANY SECURITY POLICY TO onevo_migrator;
GRANT REFERENCES TO onevo_migrator;
GRANT SELECT, ALTER ON SCHEMA::rls TO onevo_migrator;
GO
