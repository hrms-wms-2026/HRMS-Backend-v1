using Xunit;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// The harness's logins must really work and stay restricted, or every test built on them would
/// prove less than it claims.
/// </summary>
public sealed class PrivilegedRoleTestBootstrapTests
{
    private static string As(string adminConnection, string login, string password)
        => new SqlConnectionStringBuilder(adminConnection) { UserID = login, Password = password }.ConnectionString;

    [Fact]
    public async Task TestLogins_Authenticate()
    {
        var admin = await SharedSqlServerTemplate.CreateDatabaseAsync();

        foreach (var (login, password) in new[]
        {
            ("onevo_app", PrivilegedRoleTestBootstrap.AppRolePassword),
            ("onevo_migrator", PrivilegedRoleTestBootstrap.MigratorRolePassword),
        })
        {
            await using var connection = new SqlConnection(As(admin, login, password));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT USER_NAME()";

            Assert.Equal(login, await command.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task AppLogin_HasNoAdminContextFromTheHarness()
    {
        var admin = await SharedSqlServerTemplate.CreateDatabaseAsync();
        await using var connection = TestDb.CreateConnection(As(admin, "onevo_app", PrivilegedRoleTestBootstrap.AppRolePassword));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))";

        Assert.Null(await command.ExecuteScalarAsync() as string);
    }

    [Fact]
    public async Task AdminConnection_GetsAdminContext()
    {
        var admin = await SharedSqlServerTemplate.CreateDatabaseAsync();
        await using var connection = TestDb.CreateConnection(admin);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))";

        Assert.Equal("admin", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task EachCloneKeepsTheSettingsTenantIsolationNeeds()
    {
        var admin = await SharedSqlServerTemplate.CreateDatabaseAsync();
        await ONEVO.Infrastructure.Configuration.DatabaseSettingsStartupValidator.ValidateAsync(
            As(admin, "onevo_app", PrivilegedRoleTestBootstrap.AppRolePassword), default);
    }

    [Fact]
    public async Task ClonesAreIndependent()
    {
        var first = await SharedSqlServerTemplate.CreateDatabaseAsync();
        var second = await SharedSqlServerTemplate.CreateDatabaseAsync();

        Assert.NotEqual(
            new SqlConnectionStringBuilder(first).InitialCatalog,
            new SqlConnectionStringBuilder(second).InitialCatalog);
    }
}
