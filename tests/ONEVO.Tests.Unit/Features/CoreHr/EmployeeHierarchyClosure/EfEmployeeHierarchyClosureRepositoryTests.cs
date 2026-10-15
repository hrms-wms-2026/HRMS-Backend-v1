using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeHierarchyClosure;

public sealed class EfEmployeeHierarchyClosureRepositoryTests
{
    [Fact]
    public async Task RebuildAsync_ProducesDepth1ForDirectReportAndDepth2ForSkipLevel()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();

        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var employeeC = Guid.NewGuid();

        var positionA = Guid.NewGuid();
        var positionB = Guid.NewGuid();
        var positionC = Guid.NewGuid();

        // C reports to B, B reports to A.
        db.Positions.AddRange(
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = positionA, TenantId = tenantId, Name = "A", ReportsToPositionId = null },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = positionB, TenantId = tenantId, Name = "B", ReportsToPositionId = positionA },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = positionC, TenantId = tenantId, Name = "C", ReportsToPositionId = positionB });

        db.PositionAssignments.AddRange(
            CreateActivePrimary(tenantId, employeeA, positionA),
            CreateActivePrimary(tenantId, employeeB, positionB),
            CreateActivePrimary(tenantId, employeeC, positionC));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>(p => p.UtcNow == DateTimeOffset.UtcNow));
        await repository.RebuildAsync(tenantId, CancellationToken.None);

        var rows = await db.EmployeeHierarchyClosures.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync();

        Assert.Contains(rows, r => r.AncestorEmployeeId == employeeA && r.DescendantEmployeeId == employeeB && r.Depth == 1);
        Assert.Contains(rows, r => r.AncestorEmployeeId == employeeB && r.DescendantEmployeeId == employeeC && r.Depth == 1);
        Assert.Contains(rows, r => r.AncestorEmployeeId == employeeA && r.DescendantEmployeeId == employeeC && r.Depth == 2);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task RebuildAsync_FollowsAOneOffManagerPickedFromADifferentReportingLine()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        // Two branches under one root: Root -> HrLead -> HrStaff, and Root -> EngLead.
        var root = Guid.NewGuid(); var hrLead = Guid.NewGuid(); var hrStaff = Guid.NewGuid(); var engLead = Guid.NewGuid();
        var rootPos = Guid.NewGuid(); var hrLeadPos = Guid.NewGuid(); var hrStaffPos = Guid.NewGuid(); var engLeadPos = Guid.NewGuid();
        db.Positions.AddRange(
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = rootPos, TenantId = tenantId, Name = "Root" },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = hrLeadPos, TenantId = tenantId, Name = "HR Lead", ReportsToPositionId = rootPos },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = hrStaffPos, TenantId = tenantId, Name = "HR Staff", ReportsToPositionId = hrLeadPos },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = engLeadPos, TenantId = tenantId, Name = "Eng Lead", ReportsToPositionId = rootPos });
        var staffAssignment = CreateActivePrimary(tenantId, hrStaff, hrStaffPos);
        staffAssignment.ReportsToEmployeeId = engLead; // one-off: reports to the Eng Lead instead
        db.PositionAssignments.AddRange(
            CreateActivePrimary(tenantId, root, rootPos),
            CreateActivePrimary(tenantId, hrLead, hrLeadPos),
            CreateActivePrimary(tenantId, engLead, engLeadPos),
            staffAssignment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>(p => p.UtcNow == DateTimeOffset.UtcNow))
            .RebuildAsync(tenantId, CancellationToken.None);
        var rows = await db.EmployeeHierarchyClosures.AsNoTracking().Where(c => c.DescendantEmployeeId == hrStaff).ToListAsync();

        Assert.Contains(rows, r => r.AncestorEmployeeId == engLead && r.Depth == 1);
        Assert.Contains(rows, r => r.AncestorEmployeeId == root && r.Depth == 2);
        Assert.DoesNotContain(rows, r => r.AncestorEmployeeId == hrLead);
    }

    [Fact]
    public async Task RebuildAsync_StopsOnAManagerCycle()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var posA = Guid.NewGuid(); var posB = Guid.NewGuid();
        db.Positions.AddRange(
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = posA, TenantId = tenantId, Name = "A" },
            new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = posB, TenantId = tenantId, Name = "B" });
        var aAssignment = CreateActivePrimary(tenantId, a, posA);
        aAssignment.ReportsToEmployeeId = b;
        var bAssignment = CreateActivePrimary(tenantId, b, posB);
        bAssignment.ReportsToEmployeeId = a;
        db.PositionAssignments.AddRange(aAssignment, bAssignment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>(p => p.UtcNow == DateTimeOffset.UtcNow))
            .RebuildAsync(tenantId, CancellationToken.None);
        var rows = await db.EmployeeHierarchyClosures.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync();

        // Each gets their direct pick, but nobody becomes their own ancestor.
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.AncestorEmployeeId == r.DescendantEmployeeId);
    }

    [Fact]
    public async Task RebuildAsync_DeletesStaleRowsBeforeReinserting()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();

        db.EmployeeHierarchyClosures.Add(new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
        {
            TenantId = tenantId,
            AncestorEmployeeId = Guid.NewGuid(),
            DescendantEmployeeId = Guid.NewGuid(),
            Depth = 1,
            SourcePositionAssignmentId = Guid.NewGuid(),
            GeneratedAt = DateTimeOffset.UtcNow.AddDays(-30),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>(p => p.UtcNow == DateTimeOffset.UtcNow));
        await repository.RebuildAsync(tenantId, CancellationToken.None);

        var rows = await db.EmployeeHierarchyClosures.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetDirectReportEmployeeIdsAsync_ReturnsOnlyDepth1Descendants()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var skipLevelId = Guid.NewGuid();

        db.EmployeeHierarchyClosures.AddRange(
            new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure { TenantId = tenantId, AncestorEmployeeId = managerId, DescendantEmployeeId = directReportId, Depth = 1, SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow },
            new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure { TenantId = tenantId, AncestorEmployeeId = managerId, DescendantEmployeeId = skipLevelId, Depth = 2, SourcePositionAssignmentId = Guid.NewGuid(), GeneratedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>());
        var result = await repository.GetDirectReportEmployeeIdsAsync(tenantId, managerId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(directReportId, result[0]);
    }

    [Fact]
    public async Task GetDescendantEmployeeIdsAsync_ReturnsDirectAndIndirectReports()
    {
        await using var db = BuildInMemoryDb();
        var tenantId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var directId = Guid.NewGuid();
        var indirectId = Guid.NewGuid();
        db.EmployeeHierarchyClosures.AddRange(
            new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
            {
                TenantId = tenantId,
                AncestorEmployeeId = managerId,
                DescendantEmployeeId = directId,
                Depth = 1,
                SourcePositionAssignmentId = Guid.NewGuid(),
                GeneratedAt = DateTimeOffset.UtcNow
            },
            new ONEVO.Domain.Features.CoreHr.Entities.EmployeeHierarchyClosure
            {
                TenantId = tenantId,
                AncestorEmployeeId = managerId,
                DescendantEmployeeId = indirectId,
                Depth = 2,
                SourcePositionAssignmentId = Guid.NewGuid(),
                GeneratedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var repo = new EfEmployeeHierarchyClosureRepository(db, Mock.Of<IDateTimeProvider>());
        var ids = await repo.GetDescendantEmployeeIdsAsync(tenantId, managerId, CancellationToken.None);

        Assert.Contains(directId, ids);
        Assert.Contains(indirectId, ids);
    }

    private static ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment CreateActivePrimary(Guid tenantId, Guid employeeId, Guid positionId)
    {
        return new ONEVO.Domain.Features.CoreHr.Entities.PositionAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            PositionId = positionId,
            AssignmentKind = PositionAssignmentKind.PrimaryEmployment,
            AssignmentStatus = PositionAssignmentStatus.Active,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(
            currentUser.Object,
            dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options,
            auditInterceptor,
            softDeleteInterceptor,
            domainEventInterceptor,
            tenantContext.Object);
    }
}
