-- Privileged step, run before migrations by someone who can create databases and logins.
-- Migrations never create logins. Variables: DB_NAME, APP_PASSWORD, MIGRATOR_PASSWORD.
IF DB_ID(N'$(DB_NAME)') IS NULL
    CREATE DATABASE [$(DB_NAME)] COLLATE Latin1_General_100_CS_AS_SC;
GO
ALTER DATABASE [$(DB_NAME)] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
IF SUSER_ID(N'crownus_app') IS NULL
    CREATE LOGIN crownus_app WITH PASSWORD = N'$(APP_PASSWORD)';
IF SUSER_ID(N'crownus_migrator') IS NULL
    CREATE LOGIN crownus_migrator WITH PASSWORD = N'$(MIGRATOR_PASSWORD)';
GO
USE [$(DB_NAME)];
-- SESSION_CONTEXT can return wrong results in parallel plans on a reused session, so plans
-- in this database stay serial. The API refuses to start if this is changed.
ALTER DATABASE SCOPED CONFIGURATION SET MAXDOP = 1;
IF USER_ID(N'crownus_app') IS NULL
    CREATE USER crownus_app FOR LOGIN crownus_app;
IF USER_ID(N'crownus_migrator') IS NULL
    CREATE USER crownus_migrator FOR LOGIN crownus_migrator;
IF USER_ID(N'crownus_auth_base_login_fn_owner') IS NULL
    CREATE USER crownus_auth_base_login_fn_owner WITHOUT LOGIN;
ALTER ROLE db_ddladmin ADD MEMBER crownus_migrator;
ALTER ROLE db_datareader ADD MEMBER crownus_migrator;
ALTER ROLE db_datawriter ADD MEMBER crownus_migrator;
GRANT ALTER ANY SECURITY POLICY TO crownus_migrator;
GRANT REFERENCES TO crownus_migrator;
GRANT IMPERSONATE ON USER::crownus_auth_base_login_fn_owner TO crownus_migrator;
GO
