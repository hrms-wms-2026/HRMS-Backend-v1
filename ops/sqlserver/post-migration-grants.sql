-- Privileged step, run after migrations. onevo_app gets data access and one procedure, nothing
-- else: never db_owner, never ALTER ANY SECURITY POLICY, never SELECT on schema rls.
USE [$(DB_NAME)];
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO onevo_app;
GO
