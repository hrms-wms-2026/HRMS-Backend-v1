using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

public sealed class EmployeeExistsInLegalEntityAsyncTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_employee_exists_in_le_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityAId;
    private Guid _legalEntityBId;

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
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Employee Exists LE Tenant",
            Slug = "employee-exists-le",
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });
        db.LegalEntities.AddRange(
            new LegalEntity { Id = _legalEntityAId, TenantId = _tenantId, Name = "Company A" },
            new LegalEntity { Id = _legalEntityBId, TenantId = _tenantId, Name = "Company B" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ReturnsTrue_WhenEmployeeWithEmailExistsInThatLegalEntity()
    {
        await SeedEmployeeAsync(_legalEntityAId, "person@example.com");
        await using var db = CreateContext(_tenantId, "employee-exists-le");
        var repo = new EfEmployeeRepository(db);

        var exists = await repo.EmployeeExistsInLegalEntityAsync(_tenantId, _legalEntityAId, "person@example.com", excludeId: null);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ReturnsFalse_WhenEmployeeWithEmailExistsOnlyInADifferentLegalEntity()
    {
        await SeedEmployeeAsync(_legalEntityAId, "person@example.com");
        await using var db = CreateContext(_tenantId, "employee-exists-le");
        var repo = new EfEmployeeRepository(db);

        var exists = await repo.EmployeeExistsInLegalEntityAsync(_tenantId, _legalEntityBId, "person@example.com", excludeId: null);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task IsCaseInsensitive()
    {
        await SeedEmployeeAsync(_legalEntityAId, "Person@Example.com");
        await using var db = CreateContext(_tenantId, "employee-exists-le");
        var repo = new EfEmployeeRepository(db);

        var exists = await repo.EmployeeExistsInLegalEntityAsync(_tenantId, _legalEntityAId, "person@example.com", excludeId: null);

        exists.Should().BeTrue();
    }

    private async Task SeedEmployeeAsync(Guid legalEntityId, string email)
    {
        await using var db = CreateContext(_tenantId, "employee-exists-le");
        db.Employees.Add(new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = legalEntityId,
            UserId = Guid.NewGuid(),
            EmployeeNumber = Guid.NewGuid().ToString("N")[..8],
            FirstName = "Person",
            LastName = "Example",
            Email = email,
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new ONEVO.Application.Common.ServiceInterfaces.TenantRegistryEntry(
                tenantId.Value, slug, TenantStatus.Active, null));
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
}
