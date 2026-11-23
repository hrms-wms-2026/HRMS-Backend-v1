using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace ONEVO.Tests.Integration.SharedState;

/// <summary>One Redis container for the shared-state tests, pinned to the image the team approved.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    public const string Image = "redis:7.4-alpine";

    private readonly RedisContainer _container = new RedisBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A separate connection per simulated API instance.</summary>
    public async Task<IConnectionMultiplexer> ConnectAsync()
        => await ConnectionMultiplexer.ConnectAsync(
            ONEVO.Infrastructure.SharedState.SharedStateRegistration.RedisOptions(ConnectionString));
}
