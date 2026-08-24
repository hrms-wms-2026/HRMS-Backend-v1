using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingBatchRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_repo_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private IBulkOnboardingBatchRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(_connectionString);

        await using var db = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Bulk Onboarding Repo Tenant",
            Slug = "bulk-onboarding-repo",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        _tenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Acme Co",
            CountryCode = "US",
            CurrencyCode = "USD",
        };
        _legalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task AddAsync_PersistsBatchAndRows_ScopedToTenant()
    {
        await using var db = CreateContext();
        _repository = new EfBulkOnboardingBatchRepository(db);

        var tenantId = _tenantId;
        var batch = new BulkOnboardingBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = _legalEntityId,
            OriginalFileName = "employees.csv",
            CreatedByUserId = Guid.NewGuid(),
            TotalRows = 1,
        };
        var rows = new List<BulkOnboardingBatchRow>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BatchId = batch.Id,
                RowNumber = 1,
                RawDataJson = "{\"email\":\"a@b.com\"}",
            },
        };

        await _repository.AddAsync(batch, rows, CancellationToken.None);
        await _repository.SaveChangesAsync(CancellationToken.None);

        var fetched = await _repository.GetAsync(tenantId, batch.Id, CancellationToken.None);
        Assert.NotNull(fetched);
        var fetchedRows = await _repository.ListRowsAsync(tenantId, batch.Id, CancellationToken.None);
        Assert.Single(fetchedRows);
    }

    private ApplicationDbContext CreateContext()
    {
        var tenantContext = new TenantContextAccessor();
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
}
