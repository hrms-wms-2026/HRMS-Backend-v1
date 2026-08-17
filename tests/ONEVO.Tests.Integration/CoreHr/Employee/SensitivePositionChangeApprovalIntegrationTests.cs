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
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.CoreHr.SeatEntitlement;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using Testcontainers.PostgreSql;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Integration.CoreHr.Employee;

/// <summary>
/// End-to-end coverage for sensitive Change Position (reserve Planned + AccessGrantRequest)
/// then roles:manage approval, against real PostgreSQL. Matches the EmployeeDetail /
/// EmployeesList Testcontainers fixture (handler → repository → SQL). Requires Docker.
/// </summary>
public sealed class SensitivePositionChangeApprovalIntegrationTests : IAsyncLifetime
{
    private const string TenantSlug = "sensitive-pos-change-approval";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_sensitive_position_change_approval_test")
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

    private Guid _writerUserId;
    private Guid _managerUserId;

    private Guid _targetEmployeeId;
    private Guid _targetAssignmentId;
    private Guid _fromPositionId;
    private Guid _sensitivePositionId;

    private Guid _selfApproveEmployeeId;
    private Guid _selfApproveAssignmentId;
    private Guid _selfApproveFromPositionId;
    private Guid _selfApproveSensitivePositionId;

    private Guid _writerEmployeeId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await PrivilegedRoleTestBootstrap.EnsureRolesExistAsync(_connectionString);

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        _tenantId = Guid.NewGuid();
        _legalEntityId = Guid.NewGuid();
        _departmentId = Guid.NewGuid();
        _fromPositionId = Guid.NewGuid();
        _sensitivePositionId = Guid.NewGuid();
        _selfApproveFromPositionId = Guid.NewGuid();
        _selfApproveSensitivePositionId = Guid.NewGuid();

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
            NewPosition(_selfApproveSensitivePositionId, "Self-Approve Sensitive"));

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
        seeded.Employees.AddRange(target, selfApproveTarget, writerEmployee);

        seeded.PositionAccessTemplates.AddRange(
            NewSensitiveTemplate(_sensitivePositionId, templateRoleId),
            NewSensitiveTemplate(_selfApproveSensitivePositionId, templateRoleId));

        await seeded.SaveChangesAsync();

        var assignmentRepo = new EfPositionAssignmentRepository(seeded);
        var hireDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        _targetAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _targetEmployeeId, _fromPositionId, hireDate, _managerUserId))!.Value;
        _selfApproveAssignmentId = (await assignmentRepo.TryCreateActiveAssignmentAsync(
            _tenantId, _selfApproveEmployeeId, _selfApproveFromPositionId, hireDate, _managerUserId))!.Value;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task WriterRequestsSensitiveChange_ManagerApproves_EndsOldAndActivatesReserved()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var writerHandler = BuildChangePositionHandler(_writerUserId);

        var requestResult = await writerHandler.Handle(
            new ChangeEmployeePositionCommand(_targetEmployeeId, _sensitivePositionId, effectiveFrom, "Promotion"),
            CancellationToken.None);

        Assert.True(requestResult.IsSuccess);
        Assert.True(requestResult.Value!.PendingApproval);

        await using var afterRequest = CreateContext(_tenantId, TenantSlug);
        var oldAfterRequest = await afterRequest.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _targetAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Active, oldAfterRequest.AssignmentStatus);
        Assert.Null(oldAfterRequest.EffectiveTo);

        var planned = await afterRequest.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.EmployeeId == _targetEmployeeId
                              && a.PositionId == _sensitivePositionId
                              && a.AssignmentStatus == PositionAssignmentStatus.Planned
                              && a.AssignmentKind == PositionAssignmentKind.PrimaryEmployment);
        Assert.Equal(effectiveFrom, planned.EffectiveFrom);

        var grant = await afterRequest.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.EmployeeId == _targetEmployeeId
                              && g.ActionType == AccessGrantActionType.PositionChange);
        Assert.Equal("Pending", grant.ApprovalStatus);
        Assert.Equal("Promotion", grant.ChangeReason);
        Assert.Equal(planned.Id, grant.ReservedPositionAssignmentId);
        Assert.Equal(_writerUserId, grant.RequestedByUserId);

        var managerHandler = BuildApproveHandler(_managerUserId);
        var approveResult = await managerHandler.Handle(
            new ApproveAccessGrantRequestCommand(grant.Id), CancellationToken.None);

        Assert.True(approveResult.IsSuccess);

        await using var afterApprove = CreateContext(_tenantId, TenantSlug);
        var oldAfterApprove = await afterApprove.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _targetAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Ended, oldAfterApprove.AssignmentStatus);
        Assert.NotNull(oldAfterApprove.EffectiveTo);

        var activated = await afterApprove.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == planned.Id);
        Assert.Equal(PositionAssignmentStatus.Active, activated.AssignmentStatus);
        Assert.Equal(_sensitivePositionId, activated.PositionId);

        var grantAfter = await afterApprove.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.Id == grant.Id);
        Assert.Equal("Approved", grantAfter.ApprovalStatus);
        Assert.Equal("Promotion", grantAfter.ChangeReason);
    }

    [Fact]
    public async Task Requester_CannotApproveOwnRequest_ReturnsForbidden()
    {
        var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        var requesterHandler = BuildChangePositionHandler(_managerUserId);

        var requestResult = await requesterHandler.Handle(
            new ChangeEmployeePositionCommand(
                _selfApproveEmployeeId, _selfApproveSensitivePositionId, effectiveFrom, "Transfer"),
            CancellationToken.None);

        Assert.True(requestResult.IsSuccess);
        Assert.True(requestResult.Value!.PendingApproval);

        await using var db = CreateContext(_tenantId, TenantSlug);
        var grant = await db.AccessGrantRequests.AsNoTracking()
            .SingleAsync(g => g.EmployeeId == _selfApproveEmployeeId
                              && g.ActionType == AccessGrantActionType.PositionChange);

        var approveHandler = BuildApproveHandler(_managerUserId);
        var approveResult = await approveHandler.Handle(
            new ApproveAccessGrantRequestCommand(grant.Id), CancellationToken.None);

        Assert.False(approveResult.IsSuccess);
        Assert.Equal(403, approveResult.StatusCode);
        Assert.Equal("You cannot approve or reject a request you submitted yourself.", approveResult.Error);

        var old = await db.PositionAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == _selfApproveAssignmentId);
        Assert.Equal(PositionAssignmentStatus.Active, old.AssignmentStatus);
    }

    [Fact]
    public async Task Employee_CannotChangeOwnPosition_ReturnsForbidden()
    {
        var handler = BuildChangePositionHandler(_writerUserId);

        var result = await handler.Handle(
            new ChangeEmployeePositionCommand(
                _writerEmployeeId,
                _sensitivePositionId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "LateralMove"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("You cannot change your own position.", result.Error);
    }

    private ChangeEmployeePositionCommandHandler BuildChangePositionHandler(Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        return new ChangeEmployeePositionCommandHandler(
            new EfEmployeeRepository(db),
            new EfPositionRepository(db),
            new EfPositionAssignmentRepository(db),
            new UnitOfWork(db),
            new StubCurrentUser(_tenantId, userId, orgManage: true, sensitive: false),
            new EfAuthRepository(db),
            new EfAccessGrantRequestRepository(db),
            _clock,
            new OutboxWriter(db, _encryption, _clock),
            new EfAuthRepository(db));
    }

    private ApproveAccessGrantRequestCommandHandler BuildApproveHandler(Guid userId)
    {
        var db = CreateContext(_tenantId, TenantSlug);
        var auth = new EfAuthRepository(db);
        return new ApproveAccessGrantRequestCommandHandler(
            new EfAccessGrantRequestRepository(db),
            new EfOnboardingDraftRepository(db),
            new EfEmployeeRepository(db),
            auth,
            auth,
            new EfPositionRepository(db),
            new EfPositionAssignmentRepository(db),
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

    private EmployeeEntity NewEmployee(Guid tenantId, Guid userId, string employeeNumber, string firstName) => new()
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

    private async Task<Guid> SeedRoleWithPermissionAsync(Guid tenantId, string permissionCode)
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

    private async Task<Guid> SeedUserWithRoleAsync(
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
