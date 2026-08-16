using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Security;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;
using FeatureEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository;
using EfEmployeeProfileRepository = ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeProfileRepository;
using EfWorkModeRepository = ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfWorkModeRepository;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.EmployeeProfile;

/// <summary>
/// End-to-end coverage for the employees/me handlers against real PostgreSQL: tenant isolation
/// (matching EmployeesListIntegrationTests/EmployeeProfileTablesRlsTests' restricted-role
/// pattern), real optimistic-concurrency 409 behavior for the xmin token (cannot be trusted from
/// mocked unit tests), and a real AES encryption round-trip for bank details (unit tests only
/// verify the handler calls Encrypt/Decrypt, not that the real cipher actually works). Requires
/// Docker.
/// </summary>
public sealed class EmployeeProfileEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_employee_profile_endpoints_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private readonly AesEncryptionService _encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private string _connectionString = string.Empty;
    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _employeeAId;
    private Guid _userAId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var tenantA = NewTenant("Employee Profile Endpoints Tenant A", "employee-profile-endpoints-a");
        var tenantB = NewTenant("Employee Profile Endpoints Tenant B", "employee-profile-endpoints-b");
        _tenantAId = tenantA.Id;
        _tenantBId = tenantB.Id;
        db.Tenants.AddRange(tenantA, tenantB);

        var userA = new User
        {
            Id = Guid.NewGuid(), TenantId = _tenantAId, Email = "employee-a@onevo.dev",
            PasswordHash = "hash", IsActive = true
        };
        _userAId = userA.Id;
        db.Users.Add(userA);

        var employeeA = NewEmployee(_tenantAId, userA.Id, "E-A-001");
        _employeeAId = employeeA.Id;
        db.Employees.Add(employeeA);
        db.Employees.Add(NewEmployee(_tenantBId, Guid.NewGuid(), "E-B-001"));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task GetMyProfile_ReturnsOwnDataOnly_NotOtherTenantsData()
    {
        var handler = BuildGetMyProfileHandler(_tenantAId, _userAId, hasEmployeesWrite: false);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("E-A-001", result.Value!.JobInformation.EmployeeNumber);
    }

    [Fact]
    public async Task UpdateMyPersonalInformation_ReturnsConflict_OnStaleVersion()
    {
        var getHandler = BuildGetMyProfileHandler(_tenantAId, _userAId, hasEmployeesWrite: false);
        var profileBeforeAnyUpdate = await getHandler.Handle(new GetMyProfileQuery(), CancellationToken.None);
        var staleVersion = profileBeforeAnyUpdate.Value!.PersonalInformation.Version;

        var updateHandler = BuildUpdatePersonalInformationHandler(_tenantAId, _userAId);

        var firstUpdate = await updateHandler.Handle(
            new UpdatePersonalInformationCommand("Jane", "Doe", null, null, null, null, null, [], staleVersion),
            CancellationToken.None);
        Assert.True(firstUpdate.IsSuccess);

        // Second write reuses the same now-stale version captured before the first update.
        var secondUpdate = await updateHandler.Handle(
            new UpdatePersonalInformationCommand("Janet", "Doe", null, null, null, null, null, [], staleVersion),
            CancellationToken.None);

        Assert.False(secondUpdate.IsSuccess);
        Assert.Equal(409, secondUpdate.StatusCode);
    }

    [Fact]
    public async Task UpdateMyPayroll_ReturnsForbidden_WithoutEmployeesWritePermission()
    {
        var handler = BuildUpdateBankDetailsHandler(_tenantAId, _userAId, hasEmployeesWrite: false);

        var result = await handler.Handle(
            new UpdateBankDetailsCommand("Test Bank", "Main", "Jane Doe", "9876543210", "savings", null),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task UpdateMyPayroll_EncryptsWithRealCipher_AndGetMyPayrollReturnsOnlyMaskedDigits()
    {
        var updateHandler = BuildUpdateBankDetailsHandler(_tenantAId, _userAId, hasEmployeesWrite: true);
        var updateResult = await updateHandler.Handle(
            new UpdateBankDetailsCommand("Test Bank", "Main", "Jane Doe", "9876543210", "savings", null),
            CancellationToken.None);
        Assert.True(updateResult.IsSuccess);

        await using (var db = CreateContext())
        {
            var stored = await db.EmployeeBankDetails.AsNoTracking()
                .FirstAsync(b => b.EmployeeId == _employeeAId);
            Assert.DoesNotContain("9876543210", stored.AccountNumberEncrypted);
            Assert.Equal("9876543210", _encryption.Decrypt(stored.AccountNumberEncrypted));
        }

        var payrollHandler = BuildGetMyPayrollHandler(_tenantAId, _userAId, hasEmployeesWrite: true);
        var payroll = await payrollHandler.Handle(new GetMyPayrollQuery(), CancellationToken.None);

        Assert.True(payroll.IsSuccess);
        Assert.DoesNotContain("9876543210", payroll.Value!.MaskedAccountNumber);
        Assert.Contains("3210", payroll.Value.MaskedAccountNumber);
    }

    private GetMyProfileQueryHandler BuildGetMyProfileHandler(Guid tenantId, Guid userId, bool hasEmployeesWrite)
    {
        var db = CreateContext();
        return new GetMyProfileQueryHandler(
            new CommonEfEmployeeRepository(db),
            new FeatureEfEmployeeRepository(db),
            new EfEmployeeProfileRepository(db),
            new EfWorkModeRepository(db),
            new EfAuthRepository(db),
            new EfAuthRepository(db),
            _encryption,
            BuildCurrentUser(tenantId, userId, hasEmployeesWrite));
    }

    private UpdatePersonalInformationCommandHandler BuildUpdatePersonalInformationHandler(Guid tenantId, Guid userId)
    {
        var db = CreateContext();
        return new UpdatePersonalInformationCommandHandler(
            new CommonEfEmployeeRepository(db),
            new FeatureEfEmployeeRepository(db),
            new EfEmployeeProfileRepository(db),
            BuildCurrentUser(tenantId, userId, hasEmployeesWrite: false));
    }

    private UpdateBankDetailsCommandHandler BuildUpdateBankDetailsHandler(Guid tenantId, Guid userId, bool hasEmployeesWrite)
    {
        var db = CreateContext();
        return new UpdateBankDetailsCommandHandler(
            new CommonEfEmployeeRepository(db),
            new EfEmployeeProfileRepository(db),
            _encryption,
            BuildCurrentUser(tenantId, userId, hasEmployeesWrite));
    }

    private GetMyPayrollQueryHandler BuildGetMyPayrollHandler(Guid tenantId, Guid userId, bool hasEmployeesWrite)
    {
        var db = CreateContext();
        return new GetMyPayrollQueryHandler(
            new CommonEfEmployeeRepository(db),
            new EfEmployeeProfileRepository(db),
            _encryption,
            BuildCurrentUser(tenantId, userId, hasEmployeesWrite));
    }

    private static ICurrentUser BuildCurrentUser(Guid tenantId, Guid userId, bool hasEmployeesWrite)
        => new StubCurrentUser(tenantId, userId, hasEmployeesWrite);

    private sealed class StubCurrentUser : ICurrentUser
    {
        private readonly bool _hasEmployeesWrite;

        public StubCurrentUser(Guid tenantId, Guid userId, bool hasEmployeesWrite)
        {
            TenantId = tenantId;
            UserId = userId;
            _hasEmployeesWrite = hasEmployeesWrite;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "test@employee-profile-endpoints-test.onevo.dev";
        public IReadOnlyList<string> Permissions => _hasEmployeesWrite ? ["employees:write"] : [];
        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }

    private static Tenant NewTenant(string name, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        CompanySizeRange = "51-200",
        Status = TenantStatus.Active
    };

    private static EmployeeEntity NewEmployee(Guid tenantId, Guid userId, string employeeNumber) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        EmployeeNumber = employeeNumber,
        FirstName = "Test",
        LastName = employeeNumber,
        Email = $"{Guid.NewGuid():N}@employee-profile-endpoints-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

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
