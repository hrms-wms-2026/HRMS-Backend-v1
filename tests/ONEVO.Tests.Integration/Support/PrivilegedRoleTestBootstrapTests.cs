using FluentAssertions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Proves PrivilegedRoleTestBootstrap's Testcontainers-only role shape: onevo_app and
/// onevo_migrator must actually be able to authenticate (Program.cs's pre-Build()
/// DatabaseConnectionStartupValidator opens a real onevo_app connection before any
/// WebApplicationFactory override can apply), while onevo_auth_base_login_fn_owner stays exactly
/// as production expects it - NOLOGIN, BYPASSRLS. Requires Docker.
/// </summary>
public sealed class PrivilegedRoleTestBootstrapTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_role_bootstrap_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task EnsureRolesExistAsync_CreatesOnevoAppAndOnevoMigrator_AsLoginRoles()
    {
        var adminConnectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString);

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        (await CanLoginAsync(connection, "onevo_app")).Should().BeTrue(
            "onevo_app must be LOGIN in Testcontainers so the pre-Build() startup validator can authenticate");
        (await CanLoginAsync(connection, "onevo_migrator")).Should().BeTrue(
            "onevo_migrator must be LOGIN in Testcontainers");
        (await CanLoginAsync(connection, "onevo_auth_base_login_fn_owner")).Should().BeFalse(
            "the function-owner role must remain NOLOGIN, matching production");

        (await BypassesRlsAsync(connection, "onevo_app")).Should().BeFalse(
            "onevo_app must not bypass RLS, matching production");
        (await BypassesRlsAsync(connection, "onevo_auth_base_login_fn_owner")).Should().BeTrue(
            "the function-owner role must remain BYPASSRLS, matching production");
    }

    [Fact]
    public async Task EnsureRolesExistAsync_DeterministicPasswords_ActuallyAuthenticate()
    {
        var adminConnectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(adminConnectionString);

        var appConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword
        }.ConnectionString;
        await using var appConnection = new NpgsqlConnection(appConnectionString);
        var openAsApp = async () => await appConnection.OpenAsync();
        await openAsApp.Should().NotThrowAsync(
            "the deterministic test password must actually authenticate onevo_app, exactly like " +
            "Program.cs's pre-Build() DatabaseConnectionStartupValidator does against DefaultConnection");

        var migratorConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "onevo_migrator",
            Password = PrivilegedRoleTestBootstrap.MigratorRolePassword
        }.ConnectionString;
        await using var migratorConnection = new NpgsqlConnection(migratorConnectionString);
        var openAsMigrator = async () => await migratorConnection.OpenAsync();
        await openAsMigrator.Should().NotThrowAsync(
            "the deterministic test password must actually authenticate onevo_migrator");
    }

    private static async Task<bool> CanLoginAsync(NpgsqlConnection connection, string roleName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolcanlogin FROM pg_roles WHERE rolname = @roleName";
        command.Parameters.AddWithValue("roleName", roleName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> BypassesRlsAsync(NpgsqlConnection connection, string roleName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rolbypassrls FROM pg_roles WHERE rolname = @roleName";
        command.Parameters.AddWithValue("roleName", roleName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
