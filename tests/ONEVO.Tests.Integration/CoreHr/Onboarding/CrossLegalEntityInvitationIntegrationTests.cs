using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Domain.Features.Auth.Entities;
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
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Onboarding;

/// <summary>
/// Exercises the cross-legal-entity invitation path at the handler/repository layer against
/// real PostgreSQL: same tenant email in a second legal entity must not be blocked once the
/// duplicate check is legal-entity-scoped.
/// </summary>
public sealed class CrossLegalEntityInvitationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_cross_le_invitation_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityAId;
    private Guid _legalEntityBId;
    private Guid _userId;
    private const string SharedEmail = "shared@example.com";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _legalEntityAId = Guid.NewGuid();
        _legalEntityBId = Guid.NewGuid();
        _userId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Cross LE Invitation Tenant",
            Slug = "cross-le-invitation",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        db.LegalEntities.AddRange(
            new LegalEntity { Id = _legalEntityAId, TenantId = _tenantId, Name = "Company A" },
            new LegalEntity { Id = _legalEntityBId, TenantId = _tenantId, Name = "Company B" });
        db.Users.Add(new User
        {
            Id = _userId,
            TenantId = _tenantId,
            Email = SharedEmail,
            PasswordHash = "existing-hash",
            FirstName = "Shared",
            LastName = "Person",
            IsActive = true,
        });
        db.WorkModes.Add(new WorkMode { Id = 1, Code = "on_site", Label = "On-Site", IsActive = true });
        db.Employees.Add(new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityAId,
            UserId = _userId,
            EmployeeNumber = "EMP-A-001",
            FirstName = "Shared",
            LastName = "Person",
            Email = SharedEmail,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task SaveDraft_AllowsSameEmailInSecondLegalEntity()
    {
        await using var db = CreateContext(_tenantId, "cross-le-invitation");
        var handler = BuildSaveHandler(db);

        var result = await handler.Handle(new SaveOnboardingDraftCommand(
            null, "Shared", "Person", SharedEmail, _legalEntityBId, null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), "EMP-B-001", 1, null, null,
            "employee_details", IfMatchVersion: null, ReportsToEmployeeId: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SaveDraft_BlocksDuplicateEmailInSameLegalEntity()
    {
        await using var db = CreateContext(_tenantId, "cross-le-invitation");
        var handler = BuildSaveHandler(db);

        var result = await handler.Handle(new SaveOnboardingDraftCommand(
            null, "Shared", "Person", SharedEmail, _legalEntityAId, null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), "EMP-A-002", 1, null, null,
            "employee_details", IfMatchVersion: null, ReportsToEmployeeId: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    private SaveOnboardingDraftCommandHandler BuildSaveHandler(ApplicationDbContext db)
    {
        var currentUser = new StubCurrentUser(_tenantId, _userId);
        var writeService = new OnboardingDraftWriteService(
            new EfOnboardingDraftRepository(db),
            new EfEmployeeRepository(db),
            null!, null!,
            new EfPositionRepository(db),
            null!,
            new EfLegalEntityRepository(db),
            new EfDepartmentRepository(db),
            null!,
            new EfWorkModeRepository(db),
            new SeatEntitlementService(db),
            null!, null!, null!, null!, null!, null!, null!, null!,
            currentUser,
            _clock,
            null!);
        return new SaveOnboardingDraftCommandHandler(writeService, currentUser);
    }

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new TenantRegistryEntry(tenantId.Value, slug, TenantStatus.Active, null));
        }

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
        public string Email => SharedEmail;
        public IReadOnlyList<string> Permissions => ["employees:write"];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
