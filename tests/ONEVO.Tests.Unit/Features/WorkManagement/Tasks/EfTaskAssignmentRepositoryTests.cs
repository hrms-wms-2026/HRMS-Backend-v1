using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class EfTaskAssignmentRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;

    public EfTaskAssignmentRepositoryTests()
    {
        _connectionString = $"Data Source=task_assignments_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    [Fact]
    public async Task AnyForEmployeeInObjectiveAsync_TrueWhenAssignedToATaskInThatObjective()
    {
        var employeeId = Guid.NewGuid();
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId,
            ShortId = "WEB-1", Title = "T", Priority = WorkTaskPriorities.Low, StatusId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow
        };
        var assignment = new TaskAssignment { Id = Guid.NewGuid(), TaskId = task.Id, UserId = Guid.NewGuid(), EmployeeId = employeeId, AssignedById = Guid.NewGuid(), AssignedAt = DateTimeOffset.UtcNow };
        await using (var db = CreateContext())
        {
            db.WorkTasks.Add(task);
            db.TaskAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfTaskAssignmentRepository(read);

        (await repo.AnyForEmployeeInObjectiveAsync(TenantId, ObjectiveId, employeeId)).Should().BeTrue();
        (await repo.AnyForEmployeeInObjectiveAsync(TenantId, ObjectiveId, Guid.NewGuid())).Should().BeFalse();
        (await repo.AnyForEmployeeInObjectiveAsync(TenantId, Guid.NewGuid(), employeeId)).Should().BeFalse();
        (await repo.AnyForEmployeeInObjectiveAsync(Guid.NewGuid(), ObjectiveId, employeeId)).Should().BeFalse();
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new TestClock()),
            new SoftDeleteInterceptor(new TestClock()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.Date);
    }
}
