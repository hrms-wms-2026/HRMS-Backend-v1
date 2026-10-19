using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class ConfigurationStartupValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<local-32-plus-character-random-secret>")]
    [InlineData("CHANGE_ME_encryption_master_key_32_chars")]
    [InlineData("change-me-encryption-master-key-value-123456")]
    [InlineData("placeholder-encryption-master-key-value-1234")]
    [InlineData("SET_VIA_ENV")]
    [InlineData("too-short")]
    public void ValidateRequiredLocalConfiguration_RejectsInvalidMasterKey(string? masterKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:MasterKey"] = masterKey
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(
                configuration,
                "Development"));
    }

    [Fact]
    public void ValidateRequiredLocalConfiguration_AcceptsLongRandomMasterKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:MasterKey"] = "unit-test-random-master-key-value-123456789"
            })
            .Build();

        ConfigurationStartupValidator.ValidateRequiredLocalConfiguration(
            configuration,
            "Test");
    }

    private const string DevTraySecret = "dev-only-tray-jwt-secret-min-32-chars-replace-in-prod";

    private static IConfiguration TraySecretConfig(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TrayApp:Jwt:Secret"] = secret })
            .Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public void ValidateTrayJwtSecret_RejectsDevPlaceholderOutsideLocalEnvironments(string environmentName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationStartupValidator.ValidateTrayJwtSecret(TraySecretConfig(DevTraySecret), environmentName));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void ValidateTrayJwtSecret_AllowsDevPlaceholderLocally(string environmentName)
    {
        ConfigurationStartupValidator.ValidateTrayJwtSecret(TraySecretConfig(DevTraySecret), environmentName);
    }

    [Fact]
    public void ValidateTrayJwtSecret_AcceptsRealSecretInProduction()
    {
        ConfigurationStartupValidator.ValidateTrayJwtSecret(
            TraySecretConfig("k3J9vQ2xLm8pR4tY7wZ1aB6cD0eF5gH2iJ8kL3mN"), "Production");
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=OnevoDb;Username=postgres;Password=x", true)]
    [InlineData("Host=localhost;Port=5432;Database=OnevoDb;Username=some_superuser_role;Password=x", true)]
    [InlineData("Host=localhost;Port=5432;Database=OnevoDb;Username=onevo_app;Password=x", false)]
    public async Task StartAsync_WarnsOnlyWhenDefaultConnectionUsernameLooksLikeSuperuser(
        string connectionString, bool expectWarning)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();

        var logger = new CapturingLogger();
        var validator = new ConfigurationStartupValidator(configuration, logger);

        await validator.StartAsync(CancellationToken.None);

        var warnedAboutRole = logger.Messages.Exists(m => m.Contains("superuser", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectWarning, warnedAboutRole);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<ConfigurationStartupValidator>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
