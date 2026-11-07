-- Privileged step, run before migrations by someone who can create databases and logins.
-- Migrations never create logins. Variables: DB_NAME, APP_PASSWORD, MIGRATOR_PASSWORD.
IF DB_ID(N'$(DB_NAME)') IS NULL
    CREATE DATABASE [$(DB_NAME)] COLLATE Latin1_General_100_CS_AS_SC;
GO
ALTER DATABASE [$(DB_NAME)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
IF SUSER_ID(N'onevo_app') IS NULL
    CREATE LOGIN onevo_app WITH PASSWORD = N'$(APP_PASSWORD)';
IF SUSER_ID(N'onevo_migrator') IS NULL
    CREATE LOGIN onevo_migrator WITH PASSWORD = N'$(MIGRATOR_PASSWORD)';
GO
USE [$(DB_NAME)];
-- SESSION_CONTEXT can return wrong results in parallel plans on a reused session, so plans
-- in this database stay serial. The API refuses to start if this is changed.
ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1;
IF USER_ID(N'onevo_app') IS NULL
    CREATE USER onevo_app FOR LOGIN onevo_app;
IF USER_ID(N'onevo_migrator') IS NULL
    CREATE USER onevo_migrator FOR LOGIN onevo_migrator;
IF USER_ID(N'onevo_auth_base_login_fn_owner') IS NULL
    CREATE USER onevo_auth_base_login_fn_owner WITHOUT LOGIN;
ALTER ROLE db_ddladmin ADD MEMBER onevo_migrator;
ALTER ROLE db_datareader ADD MEMBER onevo_migrator;
ALTER ROLE db_datawriter ADD MEMBER onevo_migrator;
GRANT ALTER ANY SECURITY POLICY TO onevo_migrator;
GRANT REFERENCES TO onevo_migrator;
GRANT IMPERSONATE ON USER::onevo_auth_base_login_fn_owner TO onevo_migrator;
GO
