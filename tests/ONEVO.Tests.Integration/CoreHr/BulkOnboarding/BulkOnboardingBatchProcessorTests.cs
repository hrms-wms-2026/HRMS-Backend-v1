using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Services.CoreHr.BulkOnboarding;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Integration.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingBatchProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_processor_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantA;
    private Guid _tenantB;
    private Guid _legalEntityA;
    private Guid _legalEntityB;
    private Guid _userA;
    private Guid _userB;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(_connectionString);

        await using var db = CreateContext(new TenantContextAccessor());
        db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });

        var tenantA = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Processor Tenant A",
            Slug = "bulk-processor-a",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        var tenantB = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Processor Tenant B",
            Slug = "bulk-processor-b",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        _tenantA = tenantA.Id;
        _tenantB = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var legalA = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantA,
            Name = "Acme A",
            CountryCode = "US",
            CurrencyCode = "USD",
        };
        var legalB = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantB,
            Name = "Acme B",
            CountryCode = "US",
            CurrencyCode = "USD",
        };
        _legalEntityA = legalA.Id;
        _legalEntityB = legalB.Id;
        db.LegalEntities.AddRange(legalA, legalB);
        await db.SaveChangesAsync();
        _userA = Guid.NewGuid();
        _userB = Guid.NewGuid();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ProcessOnce_BatchWithValidRows_CreatesOnboardingDraftsAndMarksBatchDone()
    {
        var batch = await SeedValidatedBatchWithTwoValidRowsAsync(_tenantA, _legalEntityA, _userA);

        var processor = CreateProcessor();
        await processor.ProcessOnceAsync(CancellationToken.None);

        await using var db = CreateContext(new TenantContextAccessor());
        var reloaded = await db.Set<BulkOnboardingBatch>().AsNoTracking().SingleAsync(b => b.Id == batch.Id);
        Assert.Equal(BulkOnboardingBatchStatus.DraftsCreated, reloaded.Status);
        var rows = await db.Set<BulkOnboardingBatchRow>().AsNoTracking().Where(r => r.BatchId == batch.Id).ToListAsync();
        Assert.All(rows, r => Assert.Equal(BulkOnboardingBatchRowStatus.DraftCreated, r.Status));
        Assert.All(rows, r => Assert.NotNull(r.OnboardingDraftId));
    }

    [Fact]
    public async Task ProcessOnce_TenantAIsolatedFromTenantBBatch_NeverTouchesWrongTenantRows()
    {
        await SeedValidatedBatchWithTwoValidRowsAsync(_tenantA, _legalEntityA, _userA, createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        await SeedValidatedBatchWithTwoValidRowsAsync(_tenantB, _legalEntityB, _userB, createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var processor = CreateProcessor();
        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.ProcessOnceAsync(CancellationToken.None);

        await using var db = CreateContext(new TenantContextAccessor());
        var tenantADrafts = await db.Set<OnboardingDraftEntity>().IgnoreQueryFilters()
            .Where(d => d.TenantId == _tenantA).CountAsync();
        var tenantBDrafts = await db.Set<OnboardingDraftEntity>().IgnoreQueryFilters()
            .Where(d => d.TenantId == _tenantB).CountAsync();
        Assert.Equal(2, tenantADrafts);
        Assert.Equal(2, tenantBDrafts);
    }

    private async Task<BulkOnboardingBatch> SeedValidatedBatchWithTwoValidRowsAsync(
        Guid tenantId, Guid legalEntityId, Guid createdByUserId, DateTimeOffset? createdAt = null)
    {
        await using var db = CreateContext(new TenantContextAccessor());
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = "Email",
            ["startDate"] = "Start",
            ["employmentType"] = "Type",
        };
        var batch = new BulkOnboardingBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DefaultWorkModeId = 1,
            DefaultEmploymentType = "full_time",
            ColumnMappingJson = JsonSerializer.Serialize(mapping),
            OriginalFileName = "employees.csv",
            Status = BulkOnboardingBatchStatus.DraftCreationPending,
            TotalRows = 2,
            ValidRows = 2,
            InvalidRows = 0,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rows = new[]
        {
            NewValidRow(batch, tenantId, 1, "Jane", "Doe", $"jane-{suffix}@acme.com"),
            NewValidRow(batch, tenantId, 2, "John", "Smith", $"john-{suffix}@acme.com"),
        };
        db.Set<BulkOnboardingBatch>().Add(batch);
        db.Set<BulkOnboardingBatchRow>().AddRange(rows);
        await db.SaveChangesAsync();
        return batch;
    }

    private static BulkOnboardingBatchRow NewValidRow(
        BulkOnboardingBatch batch, Guid tenantId, int rowNumber, string first, string last, string email)
    {
        var raw = new Dictionary<string, string>
        {
            ["First Name"] = first,
            ["Last Name"] = last,
            ["Email"] = email,
            ["Start"] = "2026-09-01",
            ["Type"] = "full_time",
        };
        return new BulkOnboardingBatchRow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BatchId = batch.Id,
            RowNumber = rowNumber,
            RawDataJson = JsonSerializer.Serialize(raw),
            Status = BulkOnboardingBatchRowStatus.Valid,
        };
    }

    private BulkOnboardingBatchProcessor CreateProcessor()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new TenantContextAccessor());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped(sp => CreateContext(sp.GetRequiredService<TenantContextAccessor>()));
        services.AddScoped<IBulkOnboardingBatchRepository, EfBulkOnboardingBatchRepository>();
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<IOnboardingDraftRepository, EfOnboardingDraftRepository>();
        services.AddScoped<IEmployeeRepository, EfEmployeeRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();
        services.AddScoped<ILegalEntityRepository, EfLegalEntityRepository>();
        services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
        services.AddScoped<IWorkModeRepository, EfWorkModeRepository>();
        services.AddScoped<ISeatEntitlementService, SeatEntitlementService>();
        services.AddScoped<ICurrentUser>(_ => new StubCurrentUser());
        services.AddScoped<IDateTimeProvider>(_ => _clock);
        services.AddScoped<IOnboardingDraftWriteService>(sp => new OnboardingDraftWriteService(
            sp.GetRequiredService<IOnboardingDraftRepository>(),
            sp.GetRequiredService<IEmployeeRepository>(),
            null!, null!,
            sp.GetRequiredService<IPositionRepository>(), null!,
            sp.GetRequiredService<ILegalEntityRepository>(),
            sp.GetRequiredService<IDepartmentRepository>(),
            null!,
            sp.GetRequiredService<IWorkModeRepository>(),
            sp.GetRequiredService<ISeatEntitlementService>(),
            null!, null!, null!, null!, null!, null!, null!, null!,
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<IDateTimeProvider>()));

        var provider = services.BuildServiceProvider();
        return new BulkOnboardingBatchProcessor(provider, NullLogger<BulkOnboardingBatchProcessor>.Instance);
    }

    private ApplicationDbContext CreateContext(TenantContextAccessor tenantContext)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.Empty;
        public Guid TenantId => Guid.Empty;
        public string Email => "worker@bulk-onboarding.onevo.dev";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => false;
    }
}
