using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Tests.Unit.Features.Auth;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

/// <summary>
/// Review Focus #1 of the WM approval-revert plan: a reverter re-baselines Request.TargetUpdatedAtSnapshot
/// via a Func&lt;DateTimeOffset?&gt; closure (RevertOutcome.ReadTargetUpdatedAt) that the command handler
/// invokes AFTER the save that persists the revert - because AuditableEntityInterceptor only stamps the
/// real UpdatedAt during SaveChangesAsync, not when application code sets a field. Every reverter/handler
/// unit test in this plan mocks that closure, so none of them prove the pattern actually works against a
/// real DbContext. This test does, directly against the interceptor stack, without wiring the full
/// command handler.
/// </summary>
public sealed class RevertTargetUpdatedAtRebaselineTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;

    public RevertTargetUpdatedAtRebaselineTests()
    {
        _connectionString = $"Data Source=revert_rebaseline_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    [Fact]
    public async Task ReadTargetUpdatedAtClosure_InvokedAfterSave_SeesTheInterceptorStampedValue_NotAPreSaveSnapshot()
    {
        var moduleId = Guid.NewGuid();
        var module = new Objective
        {
            Id = moduleId, TenantId = TenantId, ProjectId = ProjectId, Title = "Original", IsActive = true,
            OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            AllocatedHours = 10m,
        };
        await using (var db = CreateContext())
        {
            db.Objectives.Add(module);
            await db.SaveChangesAsync();
        }

        await using var revert = CreateContext();
        var tracked = await revert.Objectives.FirstAsync(o => o.Id == moduleId);
        var preMutationUpdatedAt = tracked.UpdatedAt;

        // Mirrors a reverter: capture the closure before mutating/saving (RevertOutcome.Reverted(() => module.UpdatedAt)).
        Func<DateTimeOffset?> readTargetUpdatedAt = () => tracked.UpdatedAt;

        tracked.Title = "Reverted";
        await revert.SaveChangesAsync();

        var rebaselined = readTargetUpdatedAt();

        rebaselined.Should().NotBeNull();
        rebaselined.Should().NotBe(preMutationUpdatedAt, "the interceptor must have stamped a fresh UpdatedAt during this SaveChangesAsync");

        await using var reread = CreateContext();
        var persisted = await reread.Objectives.FirstAsync(o => o.Id == moduleId);
        // BeCloseTo, not Be: SQLite round-trips DateTimeOffset through TEXT storage at lower precision
        // than the in-memory CLR value the interceptor just set, so an exact-tick comparison here would
        // fail on a storage-precision artifact that doesn't exist with Postgres's timestamptz in production.
        rebaselined.Should().BeCloseTo(persisted.UpdatedAt!.Value, TimeSpan.FromMilliseconds(100),
            "the closure must see what was actually persisted, not a value computed by the caller");
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
