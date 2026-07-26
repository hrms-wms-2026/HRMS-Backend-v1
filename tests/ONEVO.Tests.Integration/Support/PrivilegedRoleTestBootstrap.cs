using Npgsql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Test-only equivalent of ops/postgres/local-bootstrap-roles.sql's role-creation step. Production
/// EF migrations (see 20260724174557_AddAuthLookupBaseLoginCandidatesFunction) assume
/// onevo_auth_base_login_fn_owner/onevo_app already exist and fail loudly if they do not - role
/// provisioning is a separate deploy-time step, never something a migration does silently. Every
/// Testcontainers-backed integration test therefore must call this once, against its own ephemeral
/// database's superuser connection, before running EF migrations - mirroring the documented
/// production deploy order (1. bootstrap privileged roles, 2. run migrations, 3. run the API).
/// </summary>
public static class PrivilegedRoleTestBootstrap
{
    public static async Task EnsureRolesExistAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'onevo_auth_base_login_fn_owner') THEN
                    CREATE ROLE onevo_auth_base_login_fn_owner
                        NOLOGIN BYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'onevo_app') THEN
                    CREATE ROLE onevo_app
                        NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
                END IF;
            END
            $$;
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}
