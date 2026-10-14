using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
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
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Services.CoreHr;
using ONEVO.Tests.Integration.Support;

namespace ONEVO.Tests.Integration.WorkManagement;

/// <summary>One cloned database per test. Seeds through a superuser context (RLS bypassed) and
/// runs the code under test as onevo_app (NOBYPASSRLS), so RLS defects surface.</summary>
public sealed class OfficeProjectDb : IAsyncDisposable
{
    private readonly SystemDateTimeProvider _clock = new();
    private string _adminConnectionString = string.Empty;
    private string _appConnectionString = string.Empty;
    private IntegrationTestEnvironmentScope _environmentScope = null!;

    public Guid TenantId { get; private set; }
    public Guid LegalEntityId { get; private set; }

    public static async Task<OfficeProjectDb> CreateAsync()
    {
        var harness = new OfficeProjectDb();
        harness._adminConnectionString = await SharedPostgresTemplate.CreateDatabaseAsync();
        harness._environmentScope = new IntegrationTestEnvironmentScope(harness._adminConnectionString);
        harness._appConnectionString = harness._environmentScope.DefaultConnectionString;

        await using (var connection = new NpgsqlConnection(harness._adminConnectionString))
        {
            await connection.OpenAsync();
            await using var grant = connection.CreateCommand();
            grant.CommandText = """
                GRANT USAGE ON SCHEMA public TO onevo_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO onevo_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO onevo_app;
                """;
            await grant.ExecuteNonQueryAsync();
        }

        await using var db = harness.AdminContext();
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == EmploymentStatusIds.Active))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Active, Code = "active", Label = "Active" });
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == EmploymentStatusIds.Terminated))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = EmploymentStatusIds.Terminated, Code = "terminated", Label = "Terminated" });

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Name = "Office Tenant", Slug = $"office-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "51-200", Status = TenantStatus.Active,
        };
        harness.TenantId = tenant.Id;
        db.Tenants.Add(tenant);

        var legalEntity = NewLegalEntity(tenant.Id, isPrimary: true);
        harness.LegalEntityId = legalEntity.Id;
        db.LegalEntities.Add(legalEntity);
        await db.SaveChangesAsync();
        return harness;
    }

    private static LegalEntity NewLegalEntity(Guid tenantId, bool isPrimary) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Name = $"Company {Guid.NewGuid():N}"[..16], IsPrimary = isPrimary,
        Timezone = "UTC", WorkStartTime = new TimeOnly(9, 0), WorkEndTime = new TimeOnly(17, 0),
        StandardWorkingDays = "[1,2,3,4,5]", BreakDurationMinutes = 60,
    };

    public ApplicationDbContext AdminContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_adminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    /// <summary>Production-like wiring under onevo_app: scoped tenant context, RLS interceptor,
    /// tenant switcher, the real provisioner and reconciler.</summary>
    public IServiceScopeFactory BuildAppScopeFactory(Action<IServiceCollection>? configure = null) =>
        BuildAppServices(configure).GetRequiredService<IServiceScopeFactory>();

    public IServiceProvider BuildAppServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider>(_clock);
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<TenantRlsInterceptor>();
        services.AddScoped(_ => new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock));
        services.AddScoped(_ => new SoftDeleteInterceptor(_clock));
        services.AddScoped(_ => new DomainEventDispatchInterceptor(new NoOpPublisher()));
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseNpgsql(_appConnectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<TenantRlsInterceptor>()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<ITenantContextSwitcher, TenantContextSwitcher>();
        services.AddScoped<IOfficeProjectProvisioner, OfficeProjectProvisioner>();
        services.AddScoped<IOfficeProjectReconciler, OfficeProjectReconciler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public async Task<Employee> SeedEmployeeAsync(Guid? legalEntityId = null, int employmentStatusId = EmploymentStatusIds.Active, Guid? userId = null)
    {
        await using var db = AdminContext();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId ?? Guid.NewGuid(),
            EmployeeNumber = $"OF-{Guid.NewGuid():N}"[..12], FirstName = "Office",
            LastName = $"Person{Guid.NewGuid():N}"[..14], Email = $"{Guid.NewGuid():N}@office-test.onevo.dev",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
            LegalEntityId = legalEntityId, EmploymentStatusId = employmentStatusId,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public async Task<Guid> SeedSecondaryLegalEntityAsync()
    {
        await using var db = AdminContext();
        var entity = NewLegalEntity(TenantId, isPrimary: false);
        db.LegalEntities.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<Guid> SeedOwnerRoleAsync()
    {
        await using var db = AdminContext();
        var role = new Role { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Owner", Description = "Full access", IsSystem = true };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, DateTimeOffset assignedAt, DateTimeOffset? expiresAt = null)
    {
        await using var db = AdminContext();
        db.UserRoles.Add(new UserRole
        {
            TenantId = TenantId, UserId = userId, RoleId = roleId,
            AssignedAt = assignedAt, AssignedBy = userId, ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    public async Task SeedTenantStatusAsync(TenantStatus status)
    {
        await using var db = AdminContext();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == TenantId);
        tenant.Status = status;
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _environmentScope.DisposeAsync();
}
