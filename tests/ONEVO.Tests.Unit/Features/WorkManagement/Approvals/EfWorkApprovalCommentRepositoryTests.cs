using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public sealed class EfWorkApprovalCommentRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public EfWorkApprovalCommentRepositoryTests()
    {
        _connectionString = $"Data Source=approval_comments_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    private static WorkApprovalComment NewComment(string subjectType, Guid subjectId, string content = "c", Guid? tenantId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, ProjectId = ProjectId, SubjectType = subjectType,
        SubjectId = subjectId, EmployeeId = Guid.NewGuid(), Content = content
    };

    [Fact]
    public async Task CountBySubjects_CountsPerSubjectOfThatTypeAndTenant()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var deleted = NewComment(WorkApprovalCommentSubjects.Approval, a);
        await using (var db = CreateContext())
        {
            db.WorkApprovalComments.AddRange(
                NewComment(WorkApprovalCommentSubjects.Approval, a),
                NewComment(WorkApprovalCommentSubjects.Approval, a),
                NewComment(WorkApprovalCommentSubjects.Approval, b),
                NewComment(WorkApprovalCommentSubjects.Invitation, a),                   // other subject type
                NewComment(WorkApprovalCommentSubjects.Approval, a, tenantId: Guid.NewGuid()), // other tenant
                deleted);
            await db.SaveChangesAsync();
            db.WorkApprovalComments.Remove(deleted);                                      // soft delete
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var counts = await new EfWorkApprovalCommentRepository(read)
            .CountBySubjectsAsync(TenantId, WorkApprovalCommentSubjects.Approval, [a, b, Guid.NewGuid()]);

        counts.Should().HaveCount(2);
        counts[a].Should().Be(2);
        counts[b].Should().Be(1);
    }

    [Fact]
    public async Task ListBySubject_OldestFirst()
    {
        // AuditableEntityInterceptor stamps CreatedAt from the clock, so the clock is moved between saves.
        var subject = Guid.NewGuid();
        var first = NewComment(WorkApprovalCommentSubjects.Invitation, subject, "first");
        var second = NewComment(WorkApprovalCommentSubjects.Invitation, subject, "second");
        await using (var db = CreateContext())
        {
            db.WorkApprovalComments.Add(second);
            await db.SaveChangesAsync();
        }
        _clock.UtcNow = _clock.UtcNow.AddHours(-1);
        await using (var db = CreateContext())
        {
            db.WorkApprovalComments.Add(first);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var rows = await new EfWorkApprovalCommentRepository(read)
            .ListBySubjectAsync(TenantId, WorkApprovalCommentSubjects.Invitation, subject);

        rows.Select(r => r.Content).Should().Equal("first", "second");
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
