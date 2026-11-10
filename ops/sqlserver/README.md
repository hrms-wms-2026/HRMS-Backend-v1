# SQL Server setup

The order matters:

1. `bootstrap-logins.sql`, as a privileged login: creates the database with its required settings, logins and users.
2. EF migrations, as `crownus_migrator`. Migrations never create logins.
3. `post-migration-grants.sql`, as a privileged login: gives `crownus_app` its data access.

`setup-local-db.ps1 -RunMigrations` runs all three on a local server. Never run it against a shared server.

## Logins

| Login or user | Rights |
|---|---|
| `crownus_app` | Read and write data, execute named procedures. Never `db_owner`, no policy or schema rights. |
| `crownus_migrator` | DDL, `ALTER ANY SECURITY POLICY`, `REFERENCES`, and rights on the schemas it creates. Not `sysadmin`. |
| `crownus_auth_base_login_fn_owner` | No login. The login lookup procedure runs as this user. |

## Required database settings

The API refuses to start without these.

| Setting | Why |
|---|---|
| Collation `Latin1_General_100_CS_AS_SC` | Hashes, tokens and unique indexes stay case-sensitive. |
| `READ_COMMITTED_SNAPSHOT ON` | Reads don't block writes. |
| Database-scoped `MAXDOP = 1` | `SESSION_CONTEXT` can return wrong results in parallel plans on a reused session. RLS depends on it. |

Keep the server on a current cumulative update. Trace flag 11042 is Microsoft's documented fix for the same issue, but it applies to the whole instance, so it is the server owner's call.
