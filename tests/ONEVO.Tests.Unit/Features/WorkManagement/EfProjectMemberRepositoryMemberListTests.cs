using Microsoft.EntityFrameworkCore;
using MediatR;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfProjectMemberRepositoryMemberListTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ProjectMember MakeMember(
        Guid projectId, Guid userId, Guid? objectiveId = null, bool isActive = true,
        Guid? tenantId = null, DateTimeOffset? joinedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, ProjectId = projectId,
        ObjectiveId = objectiveId ?? Guid.NewGuid(), UserId = userId, EmployeeId = Guid.NewGuid(),
        IsActive = isActive, JoinedAt = joinedAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ListDistinctActiveMemberUserIdsAsync_DeduplicatesAUserWithMultipleObjectiveMemberships()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.ProjectMembers.AddRange(
            MakeMember(projectId, userId, Guid.NewGuid()),
            MakeMember(projectId, userId, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.ListDistinctActiveMemberUserIdsAsync(TenantId, [projectId], takePerProject: 5, CancellationToken.None);

        Assert.Equal([userId], result[projectId]);
    }

    [Fact]
    public async Task ListDistinctActiveMemberUserIdsAsync_ExcludesInactiveMemberships()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.ProjectMembers.Add(MakeMember(projectId, Guid.NewGuid(), isActive: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.ListDistinctActiveMemberUserIdsAsync(TenantId, [projectId], takePerProject: 5, CancellationToken.None);

        Assert.False(result.ContainsKey(projectId));
    }

    [Fact]
    public async Task ListDistinctActiveMemberUserIdsAsync_ExcludesAnotherTenant()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.ProjectMembers.Add(MakeMember(projectId, Guid.NewGuid(), tenantId: Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.ListDistinctActiveMemberUserIdsAsync(TenantId, [projectId], takePerProject: 5, CancellationToken.None);

        Assert.False(result.ContainsKey(projectId));
    }

    [Fact]
    public async Task ListDistinctActiveMemberUserIdsAsync_CapsAtTakePerProject()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;
        db.ProjectMembers.AddRange(
            MakeMember(projectId, Guid.NewGuid(), joinedAt: baseTime),
            MakeMember(projectId, Guid.NewGuid(), joinedAt: baseTime.AddMinutes(1)),
            MakeMember(projectId, Guid.NewGuid(), joinedAt: baseTime.AddMinutes(2)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.ListDistinctActiveMemberUserIdsAsync(TenantId, [projectId], takePerProject: 2, CancellationToken.None);

        Assert.Equal(2, result[projectId].Count);
    }

    [Fact]
    public async Task CountDistinctActiveMembersAsync_CountsUniqueUsersNotRows()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.ProjectMembers.AddRange(
            MakeMember(projectId, userId, Guid.NewGuid()),
            MakeMember(projectId, userId, Guid.NewGuid()),
            MakeMember(projectId, Guid.NewGuid()));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.CountDistinctActiveMembersAsync(TenantId, [projectId], CancellationToken.None);

        Assert.Equal(2, result[projectId]);
    }

    [Fact]
    public async Task CountDistinctActiveMembersAsync_ExcludesInactiveMemberships()
    {
        await using var db = BuildInMemoryDb();
        var projectId = Guid.NewGuid();
        db.ProjectMembers.Add(MakeMember(projectId, Guid.NewGuid(), isActive: false));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new EfProjectMemberRepository(db);

        var result = await repository.CountDistinctActiveMembersAsync(TenantId, [projectId], CancellationToken.None);

        Assert.False(result.ContainsKey(projectId));
    }

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();
        var tenantContext = new Mock<ITenantContext>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(
            optionsBuilder.Options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, tenantContext.Object);
    }
}
