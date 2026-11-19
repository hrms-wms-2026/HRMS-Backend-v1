using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Services.CoreHr;
using ONEVO.Infrastructure.Services.CoreHr.Offboarding;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using PositionAssignmentEntity = ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment;
using ClosureEntity = ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure;
using EntityAssetRepository = ONEVO.Application.Common.RepositoryInterfaces.IEntityAssetRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

/// <summary>
/// Skip-level visibility through the legacy IEmployeeVisibilityScopeResolver path, wired with the
/// real resolver and the real EfEmployeeRepository (InMemory) - the Dapi shape that broke:
/// CEO's position covers only the GM position (ReportingStructure coverage = direct reports
/// only), GM -> Hardware Lead -> Kiru through the employee hierarchy closure. The Employees list
/// (IEmployeeAuthorityResolver) shows Kiru to the CEO, so every per-employee check must agree.
/// </summary>
public sealed class SkipLevelEmployeeVisibilityTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _otherLegalEntityId = Guid.NewGuid();
    private readonly Guid _ceoPositionId = Guid.NewGuid();
    private readonly Guid _gmPositionId = Guid.NewGuid();
    private readonly Guid _leadPositionId = Guid.NewGuid();

    private EmployeeEntity _ceo = null!, _gm = null!, _lead = null!, _kiru = null!, _outsider = null!, _otherEntityReport = null!;

    private async Task<ApplicationDbContext> SeedDapiShapeAsync(EmployeeEntity? extra = null)
    {
        var db = BuildInMemoryDb();
        _ceo = NewEmployee("CEO", _legalEntityId);
        _gm = NewEmployee("GM", _legalEntityId);
        _lead = NewEmployee("LEAD", _legalEntityId);
        _kiru = NewEmployee("KIRU", _legalEntityId);
        _outsider = NewEmployee("OUT", _legalEntityId);
        // Reports to the GM on paper, but lives in another legal entity: coverage never spans
        // legal entities, so the list (resolved per legal entity) never shows them either.
        _otherEntityReport = NewEmployee("OTHER-LE", _otherLegalEntityId);

        db.Employees.AddRange(_ceo, _gm, _lead, _kiru, _outsider, _otherEntityReport);
        db.PositionAssignments.AddRange(
            Primary(_ceo.Id, _ceoPositionId),
            Primary(_gm.Id, _gmPositionId),
            Primary(_lead.Id, _leadPositionId),
            Primary(_kiru.Id, Guid.NewGuid()),
            Primary(_outsider.Id, Guid.NewGuid()));
        db.Set<ManagementCoverageRecord>().AddRange(
            Coverage(_ceoPositionId, _gmPositionId),
            Coverage(_gmPositionId, _leadPositionId));
        db.EmployeeHierarchyClosures.AddRange(
            Closure(_gm.Id, _lead.Id, 1),
            Closure(_gm.Id, _kiru.Id, 2),
            Closure(_lead.Id, _kiru.Id, 1),
            Closure(_gm.Id, _otherEntityReport.Id, 1));
        if (extra is not null)
            db.Employees.Add(extra);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Resolver_ExpandsCoveredPositionHolder_ToWholeReportingLineSubtree_InTheCoverageLegalEntity()
    {
        await using var db = await SeedDapiShapeAsync();

        var scope = await new EmployeeVisibilityScopeResolver(db).ResolveAsync(_tenantId, _ceo.UserId);

        Assert.Equal(_ceo.Id, scope.OwnEmployeeId);
        Assert.Equal(new HashSet<Guid> { _gmPositionId }, scope.CoveredPositionIds.ToHashSet());
        Assert.Contains(_gm.Id, scope.CoveredEmployeeIds);
        Assert.Contains(_lead.Id, scope.CoveredEmployeeIds);
        Assert.Contains(_kiru.Id, scope.CoveredEmployeeIds);
        Assert.DoesNotContain(_outsider.Id, scope.CoveredEmployeeIds);
        Assert.DoesNotContain(_otherEntityReport.Id, scope.CoveredEmployeeIds);
        Assert.DoesNotContain(_ceo.Id, scope.CoveredEmployeeIds);
    }

    [Fact]
    public async Task Resolver_IgnoresInactiveCoverageRows()
    {
        await using var db = await SeedDapiShapeAsync();
        var row = await db.Set<ManagementCoverageRecord>().SingleAsync(c => c.OwnerPositionId == _ceoPositionId);
        row.Status = ManagementCoverageRecord.StatusInactive;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = await new EmployeeVisibilityScopeResolver(db).ResolveAsync(_tenantId, _ceo.UserId);

        Assert.Empty(scope.CoveredEmployeeIds);
    }

    [Fact]
    public async Task Resolver_ExpandsDepartmentCoverage_ToEmployeesInDescendantDepartments()
    {
        await using var db = BuildInMemoryDb();
        var manager = NewEmployee("MGR", _legalEntityId);
        var managerPositionId = Guid.NewGuid();
        var parentDept = new Department { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Eng" };
        var childDept = new Department { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Hardware", ParentDepartmentId = parentDept.Id };
        var grandchildDept = new Department { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Integration", ParentDepartmentId = childDept.Id };
        var unrelatedDept = new Department { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Sales" };
        var deepEmployee = NewEmployee("DEEP", _legalEntityId);
        deepEmployee.DepartmentId = grandchildDept.Id;
        var salesEmployee = NewEmployee("SALES", _legalEntityId);
        salesEmployee.DepartmentId = unrelatedDept.Id;

        db.Employees.AddRange(manager, deepEmployee, salesEmployee);
        db.Departments.AddRange(parentDept, childDept, grandchildDept, unrelatedDept);
        db.PositionAssignments.Add(Primary(manager.Id, managerPositionId));
        db.Set<ManagementCoverageRecord>().Add(new ManagementCoverageRecord
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = managerPositionId,
            CoveredTargetType = ManagementCoverageRecord.TargetDepartment, CoveredDepartmentId = parentDept.Id,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var scope = await new EmployeeVisibilityScopeResolver(db).ResolveAsync(_tenantId, manager.UserId);

        // The covered department itself still matches through CoveredDepartmentIds (raw, exact).
        Assert.Equal(new HashSet<Guid> { parentDept.Id }, scope.CoveredDepartmentIds.ToHashSet());
        Assert.Contains(deepEmployee.Id, scope.CoveredEmployeeIds);
        Assert.DoesNotContain(salesEmployee.Id, scope.CoveredEmployeeIds);
    }

    [Fact]
    public async Task GetVisibleByIdAsync_SkipLevelEmployee_IsVisible_OutsiderIsNot()
    {
        await using var db = await SeedDapiShapeAsync();
        var scope = await new EmployeeVisibilityScopeResolver(db).ResolveAsync(_tenantId, _ceo.UserId);
        var repo = new EfEmployeeRepository(db);

        Assert.NotNull(await repo.GetVisibleByIdAsync(_tenantId, scope, _kiru.Id));
        Assert.Null(await repo.GetVisibleByIdAsync(_tenantId, scope, _outsider.Id));
    }

    [Fact]
    public async Task ListVisibleAsync_ScopeBranch_IncludesSkipLevelSubtree()
    {
        // ListOffboardingOverview reads this scope branch (no RestrictToEmployeeIds).
        await using var db = await SeedDapiShapeAsync();
        var scope = await new EmployeeVisibilityScopeResolver(db).ResolveAsync(_tenantId, _ceo.UserId);

        var (items, _) = await new EfEmployeeRepository(db).ListVisibleAsync(
            _tenantId, scope, new EmployeeListFilter(null, null, null), 1, 50);

        Assert.Equal(
            new[] { _ceo.Id, _gm.Id, _lead.Id, _kiru.Id }.OrderBy(id => id),
            items.Select(i => i.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task EmployeeReadAccessGuard_SkipLevel_Succeeds_Outsider_Forbidden()
    {
        await using var db = await SeedDapiShapeAsync();
        var guard = new EmployeeReadAccessGuard(
            CurrentUser(_ceo.UserId), new EmployeeVisibilityScopeResolver(db), new EfEmployeeRepository(db));

        var skipLevel = await guard.EnsureCanRead(_tenantId, _kiru.Id);
        var outsider = await guard.EnsureCanRead(_tenantId, _outsider.Id);

        Assert.True(skipLevel.IsSuccess);
        Assert.Equal(403, outsider.StatusCode);
    }

    [Fact]
    public async Task EmployeeManageScopeGuard_SkipLevel_Allowed_Outsider_Forbidden()
    {
        await using var db = await SeedDapiShapeAsync();
        var guard = new EmployeeManageScopeGuard(
            CurrentUser(_ceo.UserId), new EmployeeVisibilityScopeResolver(db), new EfEmployeeRepository(db));

        Assert.Null(await guard.EnsureCanManage(_tenantId, _kiru.Id));
        Assert.Equal(403, (await guard.EnsureCanManage(_tenantId, _outsider.Id))!.StatusCode);
    }

    [Fact]
    public async Task EmployeeOffboardingCoverageGuard_SkipLevel_Covered_EvenWhileOffboarding()
    {
        await using var db = await SeedDapiShapeAsync();
        var kiru = await db.Employees.SingleAsync(e => e.Id == _kiru.Id);
        kiru.EmploymentStatusId = ONEVO.Domain.Lookups.EmploymentStatusIds.Offboarding;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var guard = new EmployeeOffboardingCoverageGuard(
            new EmployeeVisibilityScopeResolver(db), new EfEmployeeRepository(db), Mock.Of<IPositionAssignmentRepository>());

        Assert.Null(await guard.EnsureCovered(_tenantId, _ceo.UserId, _kiru.Id));
        Assert.Equal(403, (await guard.EnsureCovered(_tenantId, _ceo.UserId, _outsider.Id))!.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeQueryHandler_SkipLevel_ReturnsEmployee_Outsider_Forbidden()
    {
        await using var db = await SeedDapiShapeAsync();
        var assets = new Mock<EntityAssetRepository>();
        assets.Setup(r => r.GetPrimaryFileIdsByOwnerAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var handler = new GetEmployeeQueryHandler(
            new EfEmployeeRepository(db), new EmployeeVisibilityScopeResolver(db), Mock.Of<IInvitationTokenRepository>(),
            assets.Object, CurrentUser(_ceo.UserId), clock.Object);

        var skipLevel = await handler.Handle(new GetEmployeeQuery(_kiru.Id), CancellationToken.None);
        var outsider = await handler.Handle(new GetEmployeeQuery(_outsider.Id), CancellationToken.None);

        Assert.True(skipLevel.IsSuccess);
        Assert.Equal(_kiru.Id, skipLevel.Value!.Id);
        Assert.Equal(403, outsider.StatusCode);
    }

    private ICurrentUser CurrentUser(Guid userId)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.TenantId).Returns(_tenantId);
        user.SetupGet(u => u.UserId).Returns(userId);
        user.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(false);
        return user.Object;
    }

    private EmployeeEntity NewEmployee(string number, Guid legalEntityId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        UserId = Guid.NewGuid(),
        LegalEntityId = legalEntityId,
        EmployeeNumber = number,
        FirstName = "Test",
        LastName = number,
        Email = $"{Guid.NewGuid():N}@test.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
    };

    private PositionAssignmentEntity Primary(Guid employeeId, Guid positionId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        EmployeeId = employeeId,
        PositionId = positionId,
        EffectiveFrom = new DateOnly(2026, 1, 1),
    };

    private ManagementCoverageRecord Coverage(Guid ownerPositionId, Guid coveredPositionId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        LegalEntityId = _legalEntityId,
        OwnerPositionId = ownerPositionId,
        CoveredTargetType = ManagementCoverageRecord.TargetPosition,
        CoveredPositionId = coveredPositionId,
    };

    private ClosureEntity Closure(Guid ancestorId, Guid descendantId, int depth) => new()
    {
        TenantId = _tenantId,
        AncestorEmployeeId = ancestorId,
        DescendantEmployeeId = descendantId,
        Depth = depth,
        SourcePositionAssignmentId = Guid.NewGuid(),
        GeneratedAt = DateTimeOffset.UtcNow,
    };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        var clock = new Mock<IDateTimeProvider>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
