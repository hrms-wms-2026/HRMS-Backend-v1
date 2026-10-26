using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// One cloned PostgreSQL database for outbox processor tests. Processors are wired the way
/// ONEVO.Infrastructure.DependencyInjection wires them in production: ApplicationDbContext connects
/// as the real onevo_app role (NOBYPASSRLS) with TenantRlsInterceptor, and the processor switches
/// tenant context per message through the real TenantContextSwitcher. Rows are seeded and verified
/// through a separate superuser context so assertions never depend on the processor's own session.
/// </summary>
public sealed class OutboxTestHost
{
    public static readonly AesEncryptionService Encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "outbox-processor-it-master-key-32-chars-min!!" }));

    private readonly SystemDateTimeProvider _clock = new();

    private OutboxTestHost(string adminConnectionString, string appConnectionString)
    {
        AdminConnectionString = adminConnectionString;
        AppConnectionString = appConnectionString;
    }

    public string AdminConnectionString { get; }
    public string AppConnectionString { get; }
    public Guid TenantId { get; private set; }

    public static async Task<OutboxTestHost> CreateAsync()
    {
        var admin = await SharedPostgresTemplate.CreateDatabaseAsync();
        var app = new NpgsqlConnectionStringBuilder(admin)
        {
            Username = "onevo_app",
            Password = PrivilegedRoleTestBootstrap.AppRolePassword,
        }.ConnectionString;

        var host = new OutboxTestHost(admin, app);
        await using var db = host.NewAdminContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Outbox Tenant", Slug = $"outbox-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "1-10", Status = TenantStatus.Active,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        host.TenantId = tenant.Id;
        return host;
    }

    public ApplicationDbContext NewAdminContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(AdminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    /// <summary>Seeds one due pending message whose payload carries its own id, so handlers can
    /// report which message they were given.</summary>
    public async Task<Guid> SeedPendingAsync(string type, Guid? tenantId = null, object? payload = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var db = NewAdminContext();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Type = type,
            EncryptedPayload = Encryption.Encrypt(JsonSerializer.Serialize(payload ?? new TestPayload(id))),
            TenantId = tenantId,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            CreatedAt = now.AddSeconds(-5),
            NextAttemptAt = now.AddSeconds(-5),
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<OutboxMessage> GetAsync(Guid id)
    {
        await using var db = NewAdminContext();
        return await db.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    public async Task<List<OutboxMessage>> GetAllAsync()
    {
        await using var db = NewAdminContext();
        return await db.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

    /// <summary>A processor wired like production against the onevo_app connection.
    /// <paramref name="configure"/> registers the handlers (and any fakes) under test.</summary>
    public OutboxProcessor CreateProcessor(Action<IServiceCollection> configure, int? leaseSeconds = null)
        => CreateProcessor(BuildServices(configure), leaseSeconds);

    public static OutboxProcessor CreateProcessor(IServiceProvider services, int? leaseSeconds = null)
    {
        var settings = new Dictionary<string, string?>();
        if (leaseSeconds is int lease)
            settings[OutboxLeaseSettings.LeaseSecondsKey] = lease.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new OutboxProcessor(services, configuration, NullLogger<OutboxProcessor>.Instance);
    }

    /// <summary>The production-like container the processors resolve their per-batch scope from.</summary>
    public IServiceProvider BuildServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddSingleton<IEncryptionService>(Encryption);

        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(_ => new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock));
        services.AddScoped(_ => new SoftDeleteInterceptor(_clock));
        services.AddScoped(_ => new DomainEventDispatchInterceptor(new NoOpPublisher()));
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(AppConnectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>());
        });
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<OutboxDispatchContext>();
        services.AddScoped<IOutboxDispatchContext>(sp => sp.GetRequiredService<OutboxDispatchContext>());
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Runs one processor's batches until it finds nothing due. Returns how many
    /// messages it claimed in total.</summary>
    public static async Task<int> DrainAsync(OutboxProcessor processor, CancellationToken ct)
    {
        var total = 0;
        int claimed;
        while ((claimed = await processor.ProcessBatchAsync(ct)) > 0)
            total += claimed;
        return total;
    }

    /// <summary>Re-polls with <paramref name="processor"/> until <paramref name="done"/> holds
    /// (e.g. a lease measured by the database clock has expired and the message was reclaimed).</summary>
    public static async Task DrainUntilAsync(OutboxProcessor processor, Func<bool> done, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!done())
        {
            await DrainAsync(processor, cts.Token);
            if (!done())
                await Task.Delay(200, cts.Token);
        }
    }

    /// <summary>Claims due messages as an instance that then dies without completing them - what
    /// a crashed API instance leaves behind.</summary>
    public async Task<List<OutboxMessage>> ClaimAsCrashedInstanceAsync(IServiceProvider services, string owner, TimeSpan lease)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await OutboxClaimStore.ClaimBatchAsync(db, owner, lease, batchSize: 20, CancellationToken.None);
    }
}

public sealed record TestPayload(Guid MessageId);
