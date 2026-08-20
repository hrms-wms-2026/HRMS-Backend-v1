using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.PreviewBulkOnboardingMapping;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingPreviewTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_preview_test")
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
            Name = "Bulk Onboarding Preview Tenant",
            Slug = "bulk-onboarding-preview",
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
        db.Departments.Add(new Department
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            Name = "Engineering",
            IsActive = true,
        });
        await db.SaveChangesAsync();
        _userId = Guid.NewGuid();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Preview_MappingWithKnownDepartment_ResolvesDepartmentName()
    {
        await using var db = CreateContext();
        var batchId = await UploadBatchAsync(db, "First Name,Email,Dept\nJane,jane@acme.com,Engineering\n");
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["workEmail"] = "Email",
            ["department"] = "Dept",
        };

        var result = await CreatePreviewHandler(db).Handle(
            new PreviewBulkOnboardingMappingCommand(batchId, mapping), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering", result.Value!.DepartmentName);
    }

    [Fact]
    public async Task Preview_MappingWithUnknownDepartment_ReturnsNullDepartmentNameNotError()
    {
        await using var db = CreateContext();
        var batchId = await UploadBatchAsync(db, "First Name,Email,Dept\nJane,jane@acme.com,NoSuchDept\n");
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["workEmail"] = "Email",
            ["department"] = "Dept",
        };

        var result = await CreatePreviewHandler(db).Handle(
            new PreviewBulkOnboardingMappingCommand(batchId, mapping), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DepartmentName);
    }

    private async Task<Guid> UploadBatchAsync(ApplicationDbContext db, string csv)
    {
        var upload = new UploadBulkOnboardingBatchCommandHandler(
            new EfBulkOnboardingBatchRepository(db),
            new StubCurrentUser(_tenantId, _userId),
            _clock);
        var result = await upload.Handle(
            new UploadBulkOnboardingBatchCommand("employees.csv", System.Text.Encoding.UTF8.GetBytes(csv), _legalEntityId, null, null, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    private PreviewBulkOnboardingMappingCommandHandler CreatePreviewHandler(ApplicationDbContext db) =>
        new(
            new EfBulkOnboardingBatchRepository(db),
            new EfDepartmentRepository(db),
            new EfPositionRepository(db),
            new EfWorkModeRepository(db),
            new StubCurrentUser(_tenantId, _userId));

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
        public string Email => "hr@bulk-onboarding-preview.onevo.dev";
        public IReadOnlyList<string> Permissions => ["employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
