using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingDraftCreation;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.UploadBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Commands.ValidateBulkOnboardingBatch;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.Services;
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
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingCreateDraftsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_bulk_onboarding_create_drafts_test")
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
            Name = "Bulk Onboarding Create Drafts Tenant",
            Slug = "bulk-onboarding-create-drafts",
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
        db.EmploymentTypes.Add(new EmploymentType { Id = 1, Code = "full_time", Label = "Full-Time" });
        db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });
        await db.SaveChangesAsync();
        _userId = Guid.NewGuid();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task CreateDrafts_OnValidatedBatch_SetsStatusToPending()
    {
        await using var db = CreateContext();
        var batchId = await UploadValidateAndGetBatchIdAsync(db);

        var result = await CreateHandler(db).Handle(
            new RequestBulkOnboardingDraftCreationCommand(batchId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BulkOnboardingBatchStatus.DraftCreationPending, result.Value!.Status);
    }

    [Fact]
    public async Task CreateDrafts_OnBatchNotYetValidated_Returns409()
    {
        await using var db = CreateContext();
        var batchId = await UploadBatchAsync(db, "First Name,Last Name,Email,Start,Type\nJane,Doe,jane@acme.com,2026-09-01,full_time\n");

        var result = await CreateHandler(db).Handle(
            new RequestBulkOnboardingDraftCreationCommand(batchId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    private async Task<Guid> UploadValidateAndGetBatchIdAsync(ApplicationDbContext db)
    {
        var csv = "First Name,Last Name,Email,Start,Type\nJane,Doe,jane@acme.com,2026-09-01,full_time\n";
        var batchId = await UploadBatchAsync(db, csv);
        var mapping = new Dictionary<string, string?>
        {
            ["firstName"] = "First Name",
            ["lastName"] = "Last Name",
            ["workEmail"] = "Email",
            ["startDate"] = "Start",
            ["employmentType"] = "Type",
        };

        var validate = await CreateValidateHandler(db).Handle(
            new ValidateBulkOnboardingBatchCommand(batchId, mapping), CancellationToken.None);
        Assert.True(validate.IsSuccess);
        return batchId;
    }

    private async Task<Guid> UploadBatchAsync(ApplicationDbContext db, string csv)
    {
        var upload = new UploadBulkOnboardingBatchCommandHandler(
            new EfBulkOnboardingBatchRepository(db),
            new StubCurrentUser(_tenantId, _userId),
            _clock);
        var result = await upload.Handle(
            new UploadBulkOnboardingBatchCommand("employees.csv", csv, _legalEntityId, 1, "full_time", null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    private RequestBulkOnboardingDraftCreationCommandHandler CreateHandler(ApplicationDbContext db) =>
        new(new EfBulkOnboardingBatchRepository(db), new StubCurrentUser(_tenantId, _userId));

    private ValidateBulkOnboardingBatchCommandHandler CreateValidateHandler(ApplicationDbContext db) =>
        new(
            new EfBulkOnboardingBatchRepository(db),
            new BulkOnboardingRowValidator(
                new EfDepartmentRepository(db),
                new EfPositionRepository(db),
                new EfWorkModeRepository(db),
                new EfEmploymentTypeRepository(db),
                new EfEmployeeRepository(db),
                new EfChecklistTemplateRepository(db)),
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
        public string Email => "hr@bulk-onboarding-create-drafts.onevo.dev";
        public IReadOnlyList<string> Permissions => ["employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
