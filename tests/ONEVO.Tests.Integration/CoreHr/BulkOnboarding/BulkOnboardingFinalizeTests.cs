using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize;
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

public sealed class BulkOnboardingFinalizeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_finalize_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await IntegrationDatabaseBootstrap.InitializeAsync(_connectionString);

        await using var db = CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Bulk Onboarding Finalize Tenant",
            Slug = "bulk-onboarding-finalize",
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
        _userId = Guid.NewGuid();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Finalize_WithSelectedDrafts_SetsFinalizePendingAndPersistsSelection()
    {
        await using var db = CreateContext();
        var draftIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var batchId = await SeedDraftsCreatedBatchAsync(db);

        var result = await CreateHandler(db).Handle(
            new RequestBulkOnboardingFinalizeCommand(batchId, draftIds), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BulkOnboardingBatchStatus.FinalizePending, result.Value!.Status);

        var reloaded = await db.Set<BulkOnboardingBatch>().AsNoTracking().SingleAsync(b => b.Id == batchId);
        var persisted = JsonSerializer.Deserialize<List<Guid>>(reloaded.SelectedDraftIdsJson!)!;
        Assert.Equal(draftIds, persisted);
    }

    [Fact]
    public async Task Finalize_OnBatchWhoseDraftsAreNotCreated_Returns409()
    {
        await using var db = CreateContext();
        var batchId = await SeedDraftsCreatedBatchAsync(db, status: BulkOnboardingBatchStatus.Validated);

        var result = await CreateHandler(db).Handle(
            new RequestBulkOnboardingFinalizeCommand(batchId, [Guid.NewGuid()]), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    private async Task<Guid> SeedDraftsCreatedBatchAsync(ApplicationDbContext db, string? status = null)
    {
        var batch = new BulkOnboardingBatch
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            OriginalFileName = "employees.csv",
            Status = status ?? BulkOnboardingBatchStatus.DraftsCreated,
            TotalRows = 2,
            ValidRows = 2,
            InvalidRows = 0,
            CreatedByUserId = _userId,
        };
        db.Set<BulkOnboardingBatch>().Add(batch);
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private RequestBulkOnboardingFinalizeCommandHandler CreateHandler(ApplicationDbContext db) =>
        new(new EfBulkOnboardingBatchRepository(db), new StubCurrentUser(_tenantId, _userId));

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

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "hr@bulk-onboarding-finalize.onevo.dev";
        public IReadOnlyList<string> Permissions => ["employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
