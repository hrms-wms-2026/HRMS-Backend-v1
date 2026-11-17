using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.Persistence;

/// <summary>One database per class, connected as crownus_app (RLS applies), with two tenants and a user each.</summary>
public sealed class RequestConnectionRlsFixture : IAsyncLifetime
{
    private readonly SystemDateTimeProvider _clock = new();
    private IntegrationTestEnvironmentScope _environmentScope = null!;

    public string AdminConnectionString { get; private set; } = null!;
    public string AppConnectionString { get; private set; } = null!;
    public string DatabaseName { get; private set; } = null!;
    public IServiceScopeFactory Scopes { get; private set; } = null!;
    public TenantRegistryEntry TenantA { get; private set; } = null!;
    public TenantRegistryEntry TenantB { get; private set; } = null!;
    public Guid UserA { get; private set; }
    public Guid UserB { get; private set; }

    public async Task InitializeAsync()
    {
        AdminConnectionString = await SharedSqlServerTemplate.CreateDatabaseAsync();
        _environmentScope = new IntegrationTestEnvironmentScope(AdminConnectionString);
        AppConnectionString = _environmentScope.DefaultConnectionString;
        DatabaseName = new SqlConnectionStringBuilder(AppConnectionString).InitialCatalog;

        (TenantA, UserA) = await SeedTenantAndUserAsync();
        (TenantB, UserB) = await SeedTenantAndUserAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(_ => new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock));
        services.AddScoped(_ => new SoftDeleteInterceptor(_clock));
        services.AddScoped(_ => new DomainEventDispatchInterceptor(new NoOpPublisher()));
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseTestSqlServer(TenantRlsInterceptor.WithoutSilentReconnect(AppConnectionString), sql => sql.EnableRetryOnFailure(3))
                .UseSnakeCaseNamingConvention()
                .AddTestRls(sp.GetRequiredService<TenantRlsInterceptor>()));
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        Scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public Task DisposeAsync() => _environmentScope.DisposeAsync().AsTask();

    private async Task<(TenantRegistryEntry Tenant, Guid UserId)> SeedTenantAndUserAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseTestSqlServer(AdminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ApplicationDbContext(options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock), new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()), new TenantContextAccessor());

        var slug = $"rq-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, Slug = slug, CompanySizeRange = "1-10", Status = TenantStatus.Active };
        var user = new User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Email = $"{Guid.NewGuid():N}@test.onevo.dev",
            PasswordHash = "irrelevant-hash", FirstName = "Test", LastName = "User", IsActive = true
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (new TenantRegistryEntry(tenant.Id, slug, TenantStatus.Active, null), user.Id);
    }
}

/// <summary>
/// Improvements task 02: the request keeps one connection open (RequestDbConnectionMiddleware) and the session
/// context is re-applied only where today's open-per-command behaviour would have reopened. Every scenario runs
/// both ways - "today" (EF opens per command) and "kept open" - and the rows RLS lets through must match.
/// Design: docs/superpowers/specs/next/2026-11-17-one-connection-per-request-design.md. Requires Docker.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class RequestConnectionRlsIntegrationTests : IClassFixture<RequestConnectionRlsFixture>
{
    private readonly RequestConnectionRlsFixture _f;

    public RequestConnectionRlsIntegrationTests(RequestConnectionRlsFixture fixture) => _f = fixture;

    private async Task<T> InRequestAsync<T>(bool keepOpen, Func<IServiceProvider, ApplicationDbContext, Task<T>> body)
    {
        using var scope = _f.Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (keepOpen)
            await db.Database.OpenConnectionAsync();
        return await body(scope.ServiceProvider, db);
    }

    /// <summary>The seeded users this connection may see: RLS is the only filter.</summary>
    private Task<List<Guid>> VisibleUsersAsync(ApplicationDbContext db)
        => db.Users.IgnoreQueryFilters().Where(u => u.Id == _f.UserA || u.Id == _f.UserB).Select(u => u.Id).ToListAsync();

    private static void Resolve(IServiceProvider sp, TenantRegistryEntry tenant)
        => sp.GetRequiredService<IWritableTenantContext>().Resolve(tenant);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_resolution_system_then_tenant_reads_only_that_tenant(bool keepOpen)
    {
        var (beforeResolve, afterResolve) = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            sp.GetRequiredService<IWritableTenantContext>().SetSystemMode();
            var before = await VisibleUsersAsync(db);
            Resolve(sp, _f.TenantA);
            return (before, await VisibleUsersAsync(db));
        });

        beforeResolve.Should().BeEmpty("system mode does not pass the tenant rule on users");
        afterResolve.Should().Equal(_f.UserA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Switch_outside_a_transaction_reads_the_new_tenant(bool keepOpen)
    {
        var (first, second) = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            var a = await VisibleUsersAsync(db);
            await sp.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(_f.TenantB);
            return (a, await VisibleUsersAsync(db));
        });

        first.Should().Equal(_f.UserA);
        second.Should().Equal(_f.UserB);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Context_change_without_a_switcher_is_picked_up_by_the_next_command(bool keepOpen)
    {
        var (first, second) = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            var a = await VisibleUsersAsync(db);
            Resolve(sp, _f.TenantB);
            return (a, await VisibleUsersAsync(db));
        });

        first.Should().Equal(_f.UserA);
        second.Should().Equal(_f.UserB);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Switch_inside_a_transaction_then_rollback_sees_what_it_sees_today(bool keepOpen)
    {
        var seen = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            var steps = new List<List<Guid>>();
            // Production code runs transactions as one retriable unit under the retrying strategy.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                steps.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();
                steps.Add(await VisibleUsersAsync(db));
                await sp.GetRequiredService<ITenantContextSwitcher>().SwitchToTenantAsync(_f.TenantB);
                steps.Add(await VisibleUsersAsync(db));
                await tx.RollbackAsync();
            });
            steps.Add(await VisibleUsersAsync(db));
            return steps;
        });

        // Today: A inside the transaction, B after the switch, and B after the rollback (the next command
        // reopens with the switched context). Kept open must give exactly the same.
        seen[0].Should().Equal(_f.UserA);
        seen[1].Should().Equal(_f.UserB);
        seen[2].Should().Equal(_f.UserB);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_elevation_inside_a_transaction_does_not_outlive_the_commit(bool keepOpen)
    {
        // What MeetingIntegrityInterceptor does: read the context, elevate to admin, restore, commit.
        var (elevated, afterCommit) = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            var asAdmin = new List<Guid>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var connection = db.Database.GetDbConnection();
                await using (var elevate = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin"))
                {
                    elevate.Transaction = tx.GetDbTransaction();
                    await elevate.ExecuteNonQueryAsync();
                }
                asAdmin = await VisibleUsersAsync(db);
                await using (var restore = TenantRlsInterceptor.SetTenantCommand(connection, _f.TenantA.TenantId.ToString(), "tenant"))
                {
                    restore.Transaction = tx.GetDbTransaction();
                    await restore.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            });
            return (asAdmin, await VisibleUsersAsync(db));
        });

        elevated.Should().BeEquivalentTo(new[] { _f.UserA, _f.UserB });
        afterCommit.Should().Equal(_f.UserA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_the_connection_for_a_pre_tenant_read_then_reading_as_tenant(bool keepOpen)
    {
        // What the MFA and legal login stores do: close, read before any tenant is chosen, close, continue.
        var (preTenant, asTenant) = await InRequestAsync(keepOpen, async (sp, db) =>
        {
            var writable = sp.GetRequiredService<IWritableTenantContext>();
            writable.SetSystemMode();
            await db.Database.CloseConnectionAsync();
            var before = await VisibleUsersAsync(db);
            await db.Database.CloseConnectionAsync();
            writable.Resolve(_f.TenantB);
            return (before, await VisibleUsersAsync(db));
        });

        preTenant.Should().BeEmpty();
        asTenant.Should().Equal(_f.UserB);
    }

    [Fact]
    public async Task A_pooled_connection_does_not_carry_one_request_s_tenant_into_the_next()
    {
        var first = await InRequestAsync(keepOpen: true, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            return await VisibleUsersAsync(db);
        });
        var second = await InRequestAsync(keepOpen: true, async (sp, db) =>
        {
            Resolve(sp, _f.TenantB);
            return await VisibleUsersAsync(db);
        });
        var systemAfter = await InRequestAsync(keepOpen: true, async (sp, db) =>
        {
            sp.GetRequiredService<IWritableTenantContext>().SetSystemMode();
            return await VisibleUsersAsync(db);
        });

        first.Should().Equal(_f.UserA);
        second.Should().Equal(_f.UserB);
        systemAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task A_broken_kept_open_connection_is_reopened_with_the_right_tenant()
    {
        var seen = await InRequestAsync(keepOpen: true, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            var before = await VisibleUsersAsync(db);
            var spid = await db.Database.SqlQueryRaw<short>("SELECT CAST(@@SPID AS smallint) AS [Value]").SingleAsync();
            await using (var admin = TestDb.CreateConnection(_f.AdminConnectionString))
            {
                await admin.OpenAsync();
                await using var kill = admin.CreateCommand();
                kill.CommandText = $"KILL {spid}";
                await kill.ExecuteNonQueryAsync();
            }
            SqlConnection.ClearAllPools();
            return (before, after: await VisibleUsersAsync(db));
        });

        seen.before.Should().Equal(_f.UserA);
        seen.after.Should().Equal(_f.UserA);
    }

    [Fact]
    public async Task A_kept_open_request_sets_the_context_once_and_again_only_when_it_changes()
    {
        using var counter = new SessionContextCommandCounter(_f.DatabaseName);
        var setsAfterReads = await InRequestAsync(keepOpen: true, async (sp, db) =>
        {
            Resolve(sp, _f.TenantA);
            for (var i = 0; i < 5; i++)
                await VisibleUsersAsync(db);
            var afterReads = counter.Count;
            Resolve(sp, _f.TenantB);
            await VisibleUsersAsync(db);
            await VisibleUsersAsync(db);
            return afterReads;
        });

        // One on open (system mode, before Resolve), one when the first read sees tenant A: then nothing for
        // the other four reads, and one more for the switch to B.
        setsAfterReads.Should().Be(2);
        counter.Count.Should().Be(3);
    }

    /// <summary>Counts commands that call sp_set_session_context on one database, from SqlClient's diagnostics:
    /// the interceptor sends them as raw commands, which EF's own interceptors and logs do not see.</summary>
    private sealed class SessionContextCommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _database;
        private readonly List<IDisposable> _subscriptions = new();
        private int _count;

        public SessionContextCommandCounter(string database)
        {
            _database = database;
            _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        }

        public int Count => Volatile.Read(ref _count);

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener")
                _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal))
                return;
            if (value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand command
                && command.CommandText.Contains("sp_set_session_context", StringComparison.Ordinal)
                && command.Connection?.Database == _database)
                Interlocked.Increment(ref _count);
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }

        public void Dispose()
        {
            foreach (var s in _subscriptions)
                s.Dispose();
        }
    }
}
