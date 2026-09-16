using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Identity.Tokens;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Invite;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Tenancy;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

/// <summary>
/// Shared, one-time-per-class setup for SensitivePositionChangeApprovalIntegrationTests: clones
/// the database and seeds the tenant/roles/positions/employees ONCE. xUnit's IClassFixture
/// constructs this ONCE and disposes it once after every fact in the class has run, instead of
/// IAsyncLifetime's default of once PER fact - previously this class's own InitializeAsync ran 5
/// times, once per [Fact]. A dedicated _target2* employee/assignment/position trio was added for
/// ActorWithRolesManage_BypassesApproval_ActivatesImmediatelyWithApprovedAuditRow: that fact and
/// WriterRequestsSensitiveChange_ManagerApproves_EndsOldAndActivatesReserved both used to move the
/// same shared employee into the same occupancy-capped sensitive position and both assert
/// exclusive/singular state for it, which only worked because each fact got its own fresh database
/// before this conversion.
/// </summary>
public sealed class SensitivePositionChangeApprovalIntegrationTestsFixture : IAsyncLifetime
{
    public const string TenantSlug = "sensitive-pos-change-approval";


    private readonly SystemDateTimeProvider _clock = new();
    private readonly AesEncryptionService _encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private string _connectionString = string.Empty;
    private Guid _tenantId;
    private Guid _legalEntityId;
    private Guid _departmentId;

    private Guid _writerUserId;
    private Guid _managerUserId;

    private Guid _targetEmployeeId;
    private Guid _targetAssignmentId;
    private Guid _fromPositionId;
    private Guid _sensitivePositionId;

    // Dedicated to ActorWithRolesManage_BypassesApproval_ActivatesImmediatelyWithApprovedAuditRow:
    // that fact moves its employee into _sensitivePositionId and asserts an exclusive/singular
    // AccessGrantRequest + PositionAssignment for that employee, same as
    // WriterRequestsSensitiveChange_ManagerApproves_EndsOldAndActivatesReserved does for
    // _targetEmployeeId - reusing _targetEmployeeId here would collide once both facts run against
    // one shared IClassFixture database instead of each getting a fresh one.
    private Guid _target2EmployeeId;
    private Guid _target2AssignmentId;
    private Guid _target2FromPositionId;
    private Guid _target2SensitivePositionId;

    private Guid _selfApproveEmployeeId;
    private Guid _selfApproveAssignmentId;
    private Guid _selfApproveFromPositionId;
    private Guid _selfApproveSensitivePositionId;

    private Guid _writerEmployeeId;

    public Guid TenantId => _tenantId;
    public Guid ManagerUserId => _managerUserId;
    public Guid WriterUserId => _writerUserId;
    public Guid WriterEmployeeId => _writerEmployeeId;
    public Guid TargetEmployeeId => _targetEmployeeId;
    public Guid TargetAssignmentId => _targetAssignmentId;
    public Guid SensitivePositionId => _sensitivePositionId;
    public Guid SelfApproveEmployeeId => _selfApproveEmployeeId;
    public Guid SelfApproveAssignmentId => _selfApproveAssignmentId;
    public Guid SelfApproveSensitivePositionId => _selfApproveSensitivePositionId;
    public Guid Target2EmployeeId => _target2EmployeeId;
    public Guid Target2AssignmentId => _target2AssignmentId;
    public Guid Target2SensitivePositionId => _target2SensitivePositionId;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync();

        await using var db = CreateContext();

        _tenantId = Guid.NewGuid();
        _legalEntityId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();
        _fromPositionId = Guid.NewGuid();
        _sensitivePositionId = Guid.NewGuid();
        _selfApproveFromPositionId = Guid.NewGuid();
        _selfApproveSensitivePositionId = Guid.NewGuid();
        _target2FromPositionId = Guid.NewGuid();
        _target2SensitivePositionId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Sensitive Position Change Approval Tenant",
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

        db.Positions.AddRange(
            NewPosition(_fromPositionId, "Current Seat"),
            NewPosition(_sensitivePositionId, "Sensitive Seat"),
            NewPosition(_selfApproveFromPositionId, "Self-Approve From"),
            NewPosition(_selfApproveSensitivePositionId, "Self-Approve Sensitive"),
            NewPosition(_target2FromPositionId, "Current Seat 2"),
            NewPosition(_target2SensitivePositionId, "Sensitive Seat 2"));

        await db.SaveChangesAsync();

        var templateRoleId = await SeedRoleWithPermissionAsync(_tenantId, "employees:read");
        var managerRoleId = await SeedRoleWithPermissionAsync(_tenantId, "roles:manage");
        var writerRoleId = await SeedRoleWithPermissionAsync(_tenantId, "employees:write");
        _managerUserId = await SeedUserWithRoleAsync(_tenantId, managerRoleId);
        _writerUserId = await SeedUserWithRoleAsync(_tenantId, writerRoleId);

        await using var seeded = CreateContext();
        var target = NewEmployee(_tenantId, Guid.NewGuid(), "E-TARGET", "Target");
        _targetEmployeeId = target.Id;
        var selfApproveTarget = NewEmployee(_tenantId, Guid.NewGuid(), "E-SELFAPPR", "SelfApprove");
        _selfApproveEmployeeId = selfApproveTarget.Id;
        var writerEmployee = NewEmployee(_tenantId, _writerUserId, "E-WRITER", "Writer");
        _writerEmployeeId = writerEmployee.Id;
        var target2 = NewEmployee(_tenantId, Guid.NewGuid(), "E-TARGET2", "Target2");
        _target2EmployeeId = target2.Id;
        seeded.Employees.AddRange(target, selfApproveTarget, writerEmployee, target2);

        seeded.PositionAccessTemplates.AddRange(
            NewSensitiveTemplate(_sensitivePositionId, templateRoleId),
            NewSensitiveTemplate(_selfApproveSensitivePositionId, templateRoleId),
            NewSensitiveTemplate(_target2SensitivePositionId, templateRoleId));

        await seeded.SaveChangesAsync();

        var assignmentRepo = PositionAssignmentRepositoryTestSupport.CreateRepository(seeded);
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        _targetAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _targetEmployeeId, _fromPositionId, hireDate, _managerUserId, reportsToEmployeeId: null))!.Value;
        _selfApproveAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _selfApproveEmployeeId, _selfApproveFromPositionId, hireDate, _managerUserId, reportsToEmployeeId: null))!.Value;
        _target2AssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _target2EmployeeId, _target2FromPositionId, hireDate, _managerUserId, reportsToEmployeeId: null))!.Value;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public ChangeEmployeePositionCommandHandler BuildChangePositionHandler(Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        var employees = new EfEmployeeRepository(db);
        return new ChangeEmployeePositionCommandHandler(
            employees,
            new EfPositionRepository(db),
            PositionAssignmentRepositoryTestSupport.CreateRepository(db),
            new UnitOfWork(db),
            new StubCurrentUser(_tenantId, userId, orgManage: true, sensitive: false),
            new EfPermissionRepository(db),
            new EfAccessGrantRequestRepository(db),
            _clock,
            new OutboxWriter(db, _encryption, _clock),
            new EfUserRepository(db),
            new EfTenantRepository(db),
            new EmployeeOffboardingLockGuard(employees));
    }

    public ApproveAccessGrantRequestCommandHandler BuildApproveHandler(Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        var authUsers = new EfUserRepository(db);
        var authUserRoles = new EfUserRoleRepository(db);
        return new ApproveAccessGrantRequestCommandHandler(
            new EfAccessGrantRequestRepository(db),
            new EfOnboardingDraftRepository(db),
            new EfEmployeeRepository(db),
            authUsers,
            authUserRoles,
            new EfPositionRepository(db),
            PositionAssignmentRepositoryTestSupport.CreateRepository(db),
            new EfLegalEntityRepository(db),
            new EfDepartmentRepository(db),
            new EfEmploymentTypeRepository(db),
            new EfWorkModeRepository(db),
            new SeatEntitlementService(db),
            new EfChecklistTemplateRepository(db),
            new EfEmployeeChecklistTaskRepository(db),
            new EfInvitationTokenRepository(db),
            new EfTenantRepository(db),
            new OutboxWriter(db, _encryption, _clock),
            new SecureTokenGenerator(),
            new StubCurrentUser(_tenantId, userId, orgManage: true, sensitive: false),
            _clock,
            new UnitOfWork(db));
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

    public EmployeeEntity NewEmployee(Guid tenantId, Guid userId, string employeeNumber, string firstName) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        EmployeeNumber = employeeNumber,
        FirstName = firstName,
        LastName = "Employee",
        Email = $"{Guid.NewGuid():N}@sensitive-pos-change-approval.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
        LegalEntityId = _legalEntityId,
        DepartmentId = _departmentId,
    };

    private PositionAccessTemplate NewSensitiveTemplate(Guid positionId, Guid roleId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        PositionId = positionId,
        RoleId = roleId,
        RequiresApproval = true,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public async Task<Guid> SeedRoleWithPermissionAsync(Guid tenantId, string permissionCode)
    {
        await using var db = CreateContext();
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permissionCode);
        if (permission is null)
        {
            permission = new ONEVO.Domain.Features.Auth.Entities.Permission
            {
                Id = Guid.NewGuid(),
                Code = permissionCode,
                Module = "roles",
                Description = permissionCode,
            };
            db.Permissions.Add(permission);
        }

        var roleId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        db.Roles.Add(new Role
        {
            Id = roleId,
            TenantId = tenantId,
            Name = $"Role-{roleId:N}"[..20],
            CreatedById = creatorId,
        });
        db.RolePermissions.Add(new RolePermission
        {
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permission.Id,
        });
        await db.SaveChangesAsync();
        return roleId;
    }

    private async Task<Guid> SeedUserAsync(Guid tenantId)
    {
        var userId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = $"{userId:N}@example.com",
            FirstName = "User",
            LastName = "Seed",
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    public async Task<Guid> SeedUserWithRoleAsync(
        Guid tenantId,
        Guid roleId,
        DateTimeOffset? expiresAt = null)
    {
        var userId = await SeedUserAsync(tenantId);
        await using var db = CreateContext();
        db.UserRoles.Add(new UserRole
        {
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedBy = userId,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    public ApplicationDbContext CreateContext(Guid? tenantId = null, string? slug = null)
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
        public string Email => "test@sensitive-pos-change-approval.onevo.dev";
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

/// <summary>
/// End-to-end coverage for sensitive Change Position (reserve Planned + AccessGrantRequest)
/// then roles:manage approval, against real PostgreSQL. Matches the EmployeeDetail /
/// EmployeesList Testcontainers fixture (handler → repository → SQL). Requires Docker.
/// </summary>
public sealed class SensitivePositionChangeApprovalIntegrationTests : IClassFixture<SensitivePositionChangeApprovalIntegrationTestsFixture>
{
    private readonly SensitivePositionChangeApprovalIntegrationTestsFixture _fixture;

    public SensitivePositionChangeApprovalIntegrationTests(SensitivePositionChangeApprovalIntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WriterRequestsSensitiveChange_ManagerApproves_EndsOldAndActivatesReserved()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var writerHandler = _fixture.BuildChangePositionHandler(_fixture.WriterUserId);

        var requestResult = await writerHandler.Handle(
            new ChangeEmployeePositionCommand(_fixture.TargetEmployeeId, _fixture.SensitivePositionId, effectiveFrom, "Promotion"),
            CancellationToken.None);

        Assert.True(requestResult.IsSuccess);
        Assert.True(requestResult.Value!.PendingApproval);

        await using var afterRequest = _fixture.CreateContext(_fixture.TenantId, SensitivePositionChangeApprovalIntegrationTestsFixture.TenantSlug);
        var oldAfterRequest = await afterRequest.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _fixture.TargetAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Active, oldAfterRequest.AssignmentStatus);
        Assert.Null(oldAfterRequest.EffectiveTo);

        var planned = await afterRequest.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.EmployeeId == _fixture.TargetEmployeeId
                              && a.PositionId == _fixture.SensitivePositionId
                              && a.AssignmentStatus == PositionAssignmentStatus.Planned
                              && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(effectiveFrom, planned.EffectiveFrom);

        var grant = await afterRequest.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.EmployeeId == _fixture.TargetEmployeeId
                              && g.ActionType == AccessGrantActionType.PositionChange);
        Assert.Equal("Pending", grant.ApprovalStatus);
        Assert.Equal("Promotion", grant.ChangeReason);
        Assert.Equal(planned.Id, grant.ReservedPositionAssignmentId);
        Assert.Equal(_fixture.WriterUserId, grant.RequestedByUserId);

        var managerHandler = _fixture.BuildApproveHandler(_fixture.ManagerUserId);
        var approveResult = await managerHandler.Handle(
            new ApproveAccessGrantRequestCommand(grant.Id), CancellationToken.None);

        Assert.True(approveResult.IsSuccess);

        await using var afterApprove = _fixture.CreateContext(_fixture.TenantId, SensitivePositionChangeApprovalIntegrationTestsFixture.TenantSlug);
        var oldAfterApprove = await afterApprove.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _fixture.TargetAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Ended, oldAfterApprove.AssignmentStatus);
        Assert.NotNull(oldAfterApprove.EffectiveTo);

        var activated = await afterApprove.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == planned.Id);
        Assert.Equal(PositionAssignmentStatus.Active, activated.AssignmentStatus);
        Assert.Equal(_fixture.SensitivePositionId, activated.PositionId);
        Assert.Equal("Promotion", activated.ChangeReason);

        var grantAfter = await afterApprove.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.Id == grant.Id);
        Assert.Equal("Approved", grantAfter.ApprovalStatus);
        Assert.Equal("Promotion", grantAfter.ChangeReason);
    }

    [Fact]
    public async Task RequesterWithRolesManage_BypassesPendingApproval()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var requesterHandler = _fixture.BuildChangePositionHandler(_fixture.ManagerUserId);

        var requestResult = await requesterHandler.Handle(
            new ChangeEmployeePositionCommand(
                _fixture.SelfApproveEmployeeId, _fixture.SelfApproveSensitivePositionId, effectiveFrom, "Transfer"),
            CancellationToken.None);

        Assert.True(requestResult.IsSuccess);
        Assert.False(requestResult.Value!.PendingApproval);

        await using var db = _fixture.CreateContext(_fixture.TenantId, SensitivePositionChangeApprovalIntegrationTestsFixture.TenantSlug);
        var old = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _fixture.SelfApproveAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Ended, old.AssignmentStatus);

        var newAssignment = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.EmployeeId == _fixture.SelfApproveEmployeeId
                              && a.PositionId == _fixture.SelfApproveSensitivePositionId
                              && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(PositionAssignmentStatus.Active, newAssignment.AssignmentStatus);
        Assert.Equal(effectiveFrom, newAssignment.EffectiveFrom);

        var grant = await db.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.EmployeeId == _fixture.SelfApproveEmployeeId
                              && g.ActionType == AccessGrantActionType.PositionChange);
        Assert.Equal("Approved", grant.ApprovalStatus);
        Assert.Equal(_fixture.ManagerUserId, grant.RequestedByUserId);
        Assert.Equal(_fixture.ManagerUserId, grant.DecidedByUserId);
    }

    [Fact]
    public async Task Employee_CannotChangeOwnPosition_ReturnsForbidden()
    {
        var handler = _fixture.BuildChangePositionHandler(_fixture.WriterUserId);

        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(
                _fixture.WriterEmployeeId,
                _fixture.SensitivePositionId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "LateralMove"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot change your own position.", result.Error);
    }

    [Fact]
    public async Task ActorWithRolesManage_BypassesApproval_ActivatesImmediatelyWithApprovedAuditRow()
    {
        var bypassRoleId = await _fixture.SeedRoleWithPermissionAsync(_fixture.TenantId, "roles:manage");
        var bypassActorUserId = await _fixture.SeedUserWithRoleAsync(_fixture.TenantId, bypassRoleId);
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = _fixture.BuildChangePositionHandler(bypassActorUserId);

        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(_fixture.Target2EmployeeId, _fixture.Target2SensitivePositionId, effectiveFrom, "Transfer"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.PendingApproval);

        await using var db = _fixture.CreateContext(_fixture.TenantId, SensitivePositionChangeApprovalIntegrationTestsFixture.TenantSlug);
        var oldAssignment = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _fixture.Target2AssignmentId);
        Assert.Equal(PositionAssignmentStatus.Ended, oldAssignment.AssignmentStatus);
        Assert.NotNull(oldAssignment.EffectiveTo);

        var newAssignment = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.EmployeeId == _fixture.Target2EmployeeId
                              && a.PositionId == _fixture.Target2SensitivePositionId
                              && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(PositionAssignmentStatus.Active, newAssignment.AssignmentStatus);
        Assert.Equal(effectiveFrom, newAssignment.EffectiveFrom);

        var grant = await db.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.EmployeeId == _fixture.Target2EmployeeId && g.ActionType == AccessGrantActionType.PositionChange);
        Assert.Equal("Approved", grant.ApprovalStatus);
        Assert.Equal(bypassActorUserId, grant.RequestedByUserId);
        Assert.Equal(bypassActorUserId, grant.DecidedByUserId);
        Assert.NotNull(grant.DecidedAt);
        Assert.Equal("Self-authorized: requester holds roles:manage.", grant.DecisionNote);
        Assert.Equal(newAssignment.Id, grant.ReservedPositionAssignmentId);
    }

    [Fact]
    public async Task ActorWithRolesManage_CannotBypassSelfTransferBlock()
    {
        var bypassRoleId = await _fixture.SeedRoleWithPermissionAsync(_fixture.TenantId, "roles:manage");
        var bypassActorUserId = await _fixture.SeedUserWithRoleAsync(_fixture.TenantId, bypassRoleId);
        var bypassActorEmployee = _fixture.NewEmployee(_fixture.TenantId, bypassActorUserId, "E-BYPASS-SELF", "BypassSelf");

        await using (var seeded = _fixture.CreateContext())
        {
            seeded.Employees.Add(bypassActorEmployee);
            await seeded.SaveChangesAsync();
        }

        var handler = _fixture.BuildChangePositionHandler(bypassActorUserId);
        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(
                bypassActorEmployee.Id, _fixture.SensitivePositionId, DateOnly.FromDateTime(DateTime.UtcNow), "Transfer"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot change your own position.", result.Error);
    }

}
