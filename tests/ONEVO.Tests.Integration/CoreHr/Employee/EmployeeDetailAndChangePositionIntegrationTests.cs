using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Security;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

/// <summary>
/// End-to-end coverage for GetEmployeeDetailQueryHandler and ChangeEmployeePositionCommandHandler
/// against real PostgreSQL (handler → repository → SQL), matching the EmployeesList /
/// EmployeeProfile / TryCreateActiveAssignment fixture pattern. Requires Docker.
/// </summary>
public sealed class EmployeeDetailAndChangePositionIntegrationTests : IAsyncLifetime
{
    private const string TenantSlug = "employee-detail-change-pos";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_employee_detail_change_pos_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly SystemDateTimeProvider _clock = new();
    private readonly AesEncryptionService _encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _departmentId;

    private Guid _subjectEmployeeId;
    private Guid _outsiderUserId;

    private Guid _reassignEmployeeId;
    private Guid _reassignFromPositionId;
    private Guid _reassignToPositionId;
    private Guid _reassignAssignmentId;

    private Guid _capacityEmployeeId;
    private Guid _capacityFromPositionId;
    private Guid _capacityAssignmentId;
    private Guid _fullPositionId;

    private Guid _adminUserId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _adminUserId = Guid.NewGuid();
        _tenantId = Guid.NewGuid();
        _legalEntityId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Employee Detail Change Position Tenant",
            Slug = TenantSlug,
            CompanySizeRange = "51-200",
            Status = TenantStatus.Active,
        });

        db.LegalEntities.Add(new LegalEntity
        {
            Id = _legalEntityId,
            TenantId = _tenantId,
            Name = "Acme Co",
            CountryCode = "US",
            CurrencyCode = "USD",
        });

        db.Departments.Add(new Department
        {
            Id = _departmentId,
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            Name = "Engineering",
        });

        var subjectPositionId = Guid.NewGuid();
        _reassignFromPositionId = Guid.NewGuid();
        _reassignToPositionId = Guid.NewGuid();
        _capacityFromPositionId = Guid.NewGuid();
        _fullPositionId = Guid.NewGuid();

        db.Positions.AddRange(
            NewPosition(subjectPositionId, "Subject Seat"),
            NewPosition(_reassignFromPositionId, "Reassign From"),
            NewPosition(_reassignToPositionId, "Reassign To"),
            NewPosition(_capacityFromPositionId, "Capacity From"),
            NewPosition(_fullPositionId, "Full Capacity Seat"));

        var subjectUserId = Guid.NewGuid();
        _outsiderUserId = Guid.NewGuid();
        var reassignUserId = Guid.NewGuid();
        var capacityUserId = Guid.NewGuid();
        var fillerUserId = Guid.NewGuid();

        var subject = NewEmployee(_tenantId, subjectUserId, "E-SUBJECT", "Subject");
        _subjectEmployeeId = subject.Id;
        var outsider = NewEmployee(_tenantId, _outsiderUserId, "E-OUTSIDER", "Outsider");
        var reassign = NewEmployee(_tenantId, reassignUserId, "E-REASSIGN", "Reassign");
        _reassignEmployeeId = reassign.Id;
        var capacity = NewEmployee(_tenantId, capacityUserId, "E-CAPACITY", "Capacity");
        _capacityEmployeeId = capacity.Id;
        var filler = NewEmployee(_tenantId, fillerUserId, "E-FILLER", "Filler");

        db.Employees.AddRange(subject, outsider, reassign, capacity, filler);

        db.EmployeeBankDetails.Add(new EmployeeBankDetail
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeId = _subjectEmployeeId,
            BankName = "Test Bank",
            BranchName = "Main",
            AccountHolderName = "Subject Employee",
            AccountNumberEncrypted = _encryption.Encrypt("9876543210"),
            AccountType = "checking",
            IsPrimary = true,
        });

        await db.SaveChangesAsync();

        var assignmentRepo = new EfPositionAssignmentRepository(db);
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        var subjectAssignmentId = await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _subjectEmployeeId, subjectPositionId, hireDate, _adminUserId);
        Assert.NotNull(subjectAssignmentId);

        _reassignAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _reassignEmployeeId, _reassignFromPositionId, hireDate, _adminUserId))!.Value;

        _capacityAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _capacityEmployeeId, _capacityFromPositionId, hireDate, _adminUserId))!.Value;

        var fillerAssignmentId = await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, filler.Id, _fullPositionId, hireDate, _adminUserId);
        Assert.NotNull(fillerAssignmentId);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task GetDetail_WithSensitivePermission_IncludesPayroll()
    {
        var handler = BuildDetailHandler(
            orgManage: true,
            sensitive: true,
            userId: _adminUserId);

        var result = await handler.Handle(new GetEmployeeDetailQuery(_subjectEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Payroll);
        Assert.True(result.Value.Payroll!.HasBankDetailsOnFile);
        Assert.Equal("Test Bank", result.Value.Payroll.BankName);
        Assert.Contains("3210", result.Value.Payroll.MaskedAccountNumber);
        Assert.DoesNotContain("9876543210", result.Value.Payroll.MaskedAccountNumber!);
    }

    [Fact]
    public async Task GetDetail_WithoutSensitivePermission_OmitsPayroll_ButStillSucceeds()
    {
        var handler = BuildDetailHandler(
            orgManage: true,
            sensitive: false,
            userId: _adminUserId);

        var result = await handler.Handle(new GetEmployeeDetailQuery(_subjectEmployeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Payroll);
        Assert.Equal("E-SUBJECT", result.Value.JobInformation.EmployeeNumber);
        Assert.Equal("Subject", result.Value.PersonalInformation.FirstName);
    }

    [Fact]
    public async Task GetDetail_CoverageDeniedCaller_ReturnsForbidden()
    {
        var handler = BuildDetailHandler(
            orgManage: false,
            sensitive: false,
            userId: _outsiderUserId);

        var result = await handler.Handle(new GetEmployeeDetailQuery(_subjectEmployeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task ChangePosition_HappyPath_EndsOldAssignment_AndActivatesNew()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = BuildChangePositionHandler(_adminUserId);

        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(_reassignEmployeeId, _reassignToPositionId, effectiveFrom),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db = CreateContext(_tenantId, TenantSlug);
        var old = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _reassignAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Ended, old.AssignmentStatus);
        Assert.NotNull(old.EffectiveTo);

        var active = await db.PositionAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == _reassignEmployeeId
                        && a.AssignmentStatus == PositionAssignmentStatus.Active
                        && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment)
            .ToListAsync();
        Assert.Single(active);
        Assert.Equal(_reassignToPositionId, active[0].PositionId);
        Assert.Equal(effectiveFrom, active[0].EffectiveFrom);
    }

    [Fact]
    public async Task ChangePosition_FullCapacity_ReturnsConflict_AndLeavesOldAssignmentActive()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = BuildChangePositionHandler(_adminUserId);

        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(_capacityEmployeeId, _fullPositionId, effectiveFrom),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);

        await using var db = CreateContext(_tenantId, TenantSlug);
        var old = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _capacityAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Active, old.AssignmentStatus);
        Assert.Null(old.EffectiveTo);
        Assert.Equal(_capacityFromPositionId, old.PositionId);

        var activeForEmployee = await db.PositionAssignments.AsNoTracking()
            .CountAsync(a => a.EmployeeId == _capacityEmployeeId
                             && a.AssignmentStatus == PositionAssignmentStatus.Active
                             && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(1, activeForEmployee);

        var activeOnFull = await db.PositionAssignments.AsNoTracking()
            .CountAsync(a => a.PositionId == _fullPositionId
                             && a.AssignmentStatus == PositionAssignmentStatus.Active
                             && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(1, activeOnFull);
    }

    private GetEmployeeDetailQueryHandler BuildDetailHandler(bool orgManage, bool sensitive, Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        return new GetEmployeeDetailQueryHandler(
            new EfEmployeeRepository(db),
            new EmployeeVisibilityScopeResolver(db),
            new EfEmployeeProfileRepository(db),
            new EfInvitationTokenRepository(db),
            _encryption,
            new StubCurrentUser(_tenantId, userId, orgManage, sensitive),
            _clock);
    }

    private ChangeEmployeePositionCommandHandler BuildChangePositionHandler(Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        return new ChangeEmployeePositionCommandHandler(
            new EfEmployeeRepository(db),
            new EfPositionRepository(db),
            new EfPositionAssignmentRepository(db),
            new UnitOfWork(db),
            new StubCurrentUser(_tenantId, userId, orgManage: true, sensitive: false));
    }

    private Position NewPosition(Guid id, string name) => new()
    {
        Id = id,
        TenantId = _tenantId,
        LegalEntityId = _legalEntityId,
        DepartmentId = _departmentId,
        Name = name,
        PositionType = Position.TypeUnique,
        MaxOccupancy = 1,
        IsActive = true,
    };

    private EmployeeEntity NewEmployee(Guid tenantId, Guid userId, string employeeNumber, string firstName) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        EmployeeNumber = employeeNumber,
        FirstName = firstName,
        LastName = "Employee",
        Email = $"{Guid.NewGuid():N}@employee-detail-change-pos.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        LegalEntityId = _legalEntityId,
        DepartmentId = _departmentId,
    };

    private ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
    {
        var tenantContext = new TenantContextAccessor();
        if (tenantId is not null && slug is not null)
        {
            tenantContext.Resolve(new TenantRegistryEntry(
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

    private sealed class StubCurrentUser : ICurrentUser
    {
        private readonly bool _orgManage;
        private readonly bool _sensitive;

        public StubCurrentUser(Guid tenantId, Guid userId, bool orgManage, bool sensitive)
        {
            TenantId = tenantId;
            UserId = userId;
            _orgManage = orgManage;
            _sensitive = sensitive;
        }

        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string Email => "test@employee-detail-change-pos.onevo.dev";
        public IReadOnlyList<string> Permissions
        {
            get
            {
                var perms = new List<string> { "employees:read", "employees:write" };
                if (_orgManage) perms.Add("org:manage");
                if (_sensitive) perms.Add("employees:read:sensitive");
                return perms;
            }
        }

        public bool HasPermission(string permission) => Permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }
}
