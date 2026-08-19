using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingBatch;
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

public sealed class BulkOnboardingGetStatusTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_get_status_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _otherTenantId;
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
            Name = "Bulk Onboarding Status Tenant",
            Slug = "bulk-onboarding-status",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        var other = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Bulk Onboarding Other Tenant",
            Slug = "bulk-onboarding-status-other",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        };
        _tenantId = tenant.Id;
        _otherTenantId = other.Id;
        db.Tenants.AddRange(tenant, other);

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
    public async Task GetById_ReturnsBatchWithAllRowStatuses()
    {
        await using var db = CreateContext();
        var batchId = await UploadBatchAsync(db);

        var result = await CreateHandler(db, _tenantId).Handle(
            new GetBulkOnboardingBatchQuery(batchId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(batchId, result.Value!.Id);
        Assert.NotEmpty(result.Value.Rows);
    }

    [Fact]
    public async Task GetById_FromDifferentTenant_Returns404NotAnotherTenantsBatch()
    {
        await using var db = CreateContext();
        var batchId = await UploadBatchAsync(db);

        var result = await CreateHandler(db, _otherTenantId).Handle(
            new GetBulkOnboardingBatchQuery(batchId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    private async Task<Guid> UploadBatchAsync(ApplicationDbContext db)
    {
        var upload = new UploadBulkOnboardingBatchCommandHandler(
            new EfBulkOnboardingBatchRepository(db),
            new StubCurrentUser(_tenantId, _userId),
            _clock);
        var result = await upload.Handle(
            new UploadBulkOnboardingBatchCommand(
                "employees.csv",
                System.Text.Encoding.UTF8.GetBytes("First Name,Last Name,Email\nJane,Doe,jane@acme.com\n"),
                _legalEntityId, null, null, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    private GetBulkOnboardingBatchQueryHandler CreateHandler(ApplicationDbContext db, Guid tenantId) =>
        new(new EfBulkOnboardingBatchRepository(db), new StubCurrentUser(tenantId, _userId));

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
        public string Email => "hr@bulk-onboarding-status.onevo.dev";
        public IReadOnlyList<string> Permissions => ["employees:read"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
