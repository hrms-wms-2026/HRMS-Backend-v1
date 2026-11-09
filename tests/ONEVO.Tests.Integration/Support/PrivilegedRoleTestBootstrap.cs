
namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Passwords for the test logins, which ops/sqlserver/bootstrap-logins.sql creates through
/// SharedSqlServerTemplate. Testcontainers only.
/// </summary>
public static class PrivilegedRoleTestBootstrap
{
    public const string AppRolePassword = "Itest_Runtime_Pw_2026!";

    public const string MigratorRolePassword = "Itest_Schema_Pw_2026!";

    /// <summary>The template has already created every login, so there is nothing left to do.</summary>
    public static Task EnsureRolesExistAsync(string connectionString, CancellationToken ct = default)
        => Task.CompletedTask;
}
