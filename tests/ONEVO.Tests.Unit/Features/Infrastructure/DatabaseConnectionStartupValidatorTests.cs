using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public sealed class DatabaseConnectionStartupValidatorTests
{
    [Theory]
    [InlineData("DefaultConnection")]
    [InlineData("MigrationConnection")]
    public void Validate_RejectsPlaceholderPasswordsBeforeHostedServicesStart(string connectionName)
    {
        var values = ValidConnectionValues();
        values[$"ConnectionStrings:{connectionName}"] = connectionName == "DefaultConnection"
            ? "Host=localhost;Database=OnevoDb;Username=onevo_app;Password=SET_VIA_ENV"
            : "Host=localhost;Database=OnevoDb;Username=onevo_migrator;Password=SET_VIA_ENV";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.Validate(configuration, "Development"));

        Assert.Contains(connectionName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(".env.example", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ops/postgres/setup-local-db.ps1 -RunMigrations", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet run loads it directly", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsRestrictedRuntimeAndMigrationRoles()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidConnectionValues())
            .Build();

        DatabaseConnectionStartupValidator.Validate(configuration, "Development");
    }

    [Fact]
    public void Validate_DoesNotApplyLocalPlaceholderPolicyInProduction()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        DatabaseConnectionStartupValidator.Validate(configuration, "Production");
    }

    [Theory]
    [InlineData("Host=localhost;Database=OnevoDb;Username=onevo_app;Password=<local-app-password>")]
    [InlineData("Host=localhost;Database=OnevoDb;Username=onevo_app;Password=")]
    [InlineData("Host=localhost;Database=OnevoDb;Password=unit-test-value")]
    [InlineData("Host=localhost;Database=OnevoDb;Username=postgres;Password=unit-test-value")]
    [InlineData("Host=localhost;Database=OnevoDb;Username=onevo_migrator;Password=unit-test-value")]
    public void Validate_RejectsUnsafeRuntimeConnection(string runtimeConnection)
    {
        var values = ValidConnectionValues();
        values["ConnectionStrings:DefaultConnection"] = runtimeConnection;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.Validate(configuration, "Development"));
    }

    [Fact]
    public void Validate_RejectsSameUsernameForRuntimeAndMigrationConnections()
    {
        var values = ValidConnectionValues();
        values["ConnectionStrings:MigrationConnection"] =
            "Host=localhost;Database=OnevoDb;Username=onevo_app;Password=unit-test-migration";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.Validate(configuration, "Development"));
    }

    [Fact]
    public void Validate_RejectsPostgresMigrationConnection()
    {
        var values = ValidConnectionValues();
        values["ConnectionStrings:MigrationConnection"] =
            "Host=localhost;Database=OnevoDb;Username=postgres;Password=unit-test-migration";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.Validate(configuration, "Development"));
    }

    [Fact]
    public async Task ValidateAndOpenAsync_FailsWhenRestrictedRuntimeConnectionCannotOpen()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidConnectionValues())
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.ValidateAndOpenAsync(
                configuration,
                "Development",
                openConnection: (_, _) => throw new Npgsql.NpgsqlException("unit-test auth failure")));

        Assert.Contains("onevo_app", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAndOpenAsync_DoesNotOpenConnectionWhenShapeValidationFails()
    {
        var values = ValidConnectionValues();
        values["ConnectionStrings:DefaultConnection"] =
            "Host=localhost;Database=OnevoDb;Username=postgres;Password=unit-test-runtime";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var openWasCalled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.ValidateAndOpenAsync(
                configuration,
                "Development",
                openConnection: (_, _) =>
                {
                    openWasCalled = true;
                    return Task.CompletedTask;
                }));

        Assert.False(openWasCalled);
    }

    [Fact]
    public void Validate_ProcessOverrideDiagnosticDoesNotPrintConnectionSecret()
    {
        var values = ValidConnectionValues();
        values["ConnectionStrings:DefaultConnection"] =
            "Host=localhost;Database=OnevoDb;Username=postgres;Password=unit-test-secret";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionStartupValidator.Validate(configuration, "Development", true));

        Assert.Contains("process-level", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overrides the repo-root .env", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unit-test-secret", exception.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> ValidConnectionValues()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Database=OnevoDb;Username=onevo_app;Password=unit-test-runtime",
            ["ConnectionStrings:MigrationConnection"] =
                "Host=localhost;Database=OnevoDb;Username=onevo_migrator;Password=unit-test-migration"
        };
    }
}
