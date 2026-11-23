-- Read-only check, run after post-migration-grants.sql on any deployed database (task 09).
-- Lists every right crownus_app or crownus_migrator has beyond the design in ops/sqlserver/README.md.
-- No rows and "Login rights OK" = pass. Any finding ends with an error, so `sqlcmd -b` exits 1.
--
--   sqlcmd -S <server>,<port> -U <admin login> -C -b -i ops/sqlserver/check-login-rights.sql -v DB_NAME=<database>
--
-- Needs a login that can read server role membership (the same admin login as the grants step).
USE [$(DB_NAME)];
SET NOCOUNT ON;

DECLARE @findings TABLE (principal sysname, finding nvarchar(400));

-- 1. Server roles: neither login may be a server-level admin.
INSERT @findings
SELECT l.name, N'member of server role ' + r.name
FROM sys.server_role_members m
JOIN sys.server_principals r ON r.principal_id = m.role_principal_id
JOIN sys.server_principals l ON l.principal_id = m.member_principal_id
WHERE l.name IN (N'crownus_app', N'crownus_migrator');

-- 2. Database owner: the database must not be owned by either login.
INSERT @findings
SELECT SUSER_SNAME(d.owner_sid), N'owns the database'
FROM sys.databases d
WHERE d.name = DB_NAME() AND SUSER_SNAME(d.owner_sid) IN (N'crownus_app', N'crownus_migrator');

-- 3. Database roles: crownus_app has none; crownus_migrator only db_ddladmin, db_datareader, db_datawriter.
INSERT @findings
SELECT u.name, N'member of database role ' + r.name
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
WHERE u.name = N'crownus_app'
   OR (u.name = N'crownus_migrator' AND r.name NOT IN (N'db_ddladmin', N'db_datareader', N'db_datawriter'));

-- 4. crownus_app permissions: only CONNECT, data access on schema dbo and the one login lookup procedure.
--    In particular never ALTER ANY SECURITY POLICY, CONTROL, ALTER, or anything on schema rls.
INSERT @findings
SELECT u.name,
       p.state_desc + N' ' + p.permission_name + N' on ' + p.class_desc
       + COALESCE(N' ' + CASE p.class
           WHEN 3 THEN SCHEMA_NAME(p.major_id)
           WHEN 1 THEN OBJECT_SCHEMA_NAME(p.major_id) + N'.' + OBJECT_NAME(p.major_id)
         END, N'')
FROM sys.database_permissions p
JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
WHERE u.name = N'crownus_app'
  AND p.state IN ('G', 'W')
  AND NOT (p.class = 0 AND p.permission_name = N'CONNECT')
  AND NOT (p.class = 3 AND SCHEMA_NAME(p.major_id) = N'dbo'
           AND p.permission_name IN (N'SELECT', N'INSERT', N'UPDATE', N'DELETE'))
  AND NOT (p.class = 1 AND p.permission_name = N'EXECUTE'
           AND p.major_id = OBJECT_ID(N'auth_internal.auth_lookup_base_login_candidates'));

-- 5. Ownership: crownus_app owns no schema and no object (an owner bypasses grants and can alter RLS objects).
INSERT @findings
SELECT p.name, N'owns schema ' + s.name
FROM sys.schemas s JOIN sys.database_principals p ON p.principal_id = s.principal_id
WHERE p.name = N'crownus_app';

INSERT @findings
SELECT p.name, N'owns object ' + OBJECT_SCHEMA_NAME(o.object_id) + N'.' + o.name
FROM sys.objects o JOIN sys.database_principals p ON p.principal_id = o.principal_id
WHERE p.name = N'crownus_app';

-- 6. The grants step ran: crownus_app can read and write dbo and run the login lookup.
INSERT @findings
SELECT N'crownus_app', N'missing ' + x.permission_name + N' on schema dbo (run post-migration-grants.sql)'
FROM (VALUES (N'SELECT'), (N'INSERT'), (N'UPDATE'), (N'DELETE')) x(permission_name)
WHERE NOT EXISTS (
    SELECT 1 FROM sys.database_permissions p
    JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
    WHERE u.name = N'crownus_app' AND p.class = 3 AND SCHEMA_NAME(p.major_id) = N'dbo'
      AND p.permission_name = x.permission_name AND p.state IN ('G', 'W'));

IF NOT EXISTS (
    SELECT 1 FROM sys.database_permissions p
    JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
    WHERE u.name = N'crownus_app' AND p.class = 1 AND p.permission_name = N'EXECUTE'
      AND p.major_id = OBJECT_ID(N'auth_internal.auth_lookup_base_login_candidates'))
    INSERT @findings VALUES (N'crownus_app', N'missing EXECUTE on auth_internal.auth_lookup_base_login_candidates (run post-migration-grants.sql)');

IF EXISTS (SELECT 1 FROM @findings)
BEGIN
    SELECT principal, finding FROM @findings ORDER BY principal, finding;
    RAISERROR(N'Login rights check FAILED: see the findings above.', 16, 1);
END
ELSE
    PRINT N'Login rights OK: crownus_app has data access only, crownus_migrator has no admin role.';
GO
