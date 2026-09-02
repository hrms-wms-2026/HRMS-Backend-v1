using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using MediatR;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Unit.Features.Auth;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

/// <summary>
/// Real-DbContext coverage for the extend_allocation approve path. Moq-based handler tests cannot
/// catch EF Core duplicate-tracking failures (see plan 2026-08-17 allocation-overcommit fix).
/// </summary>
public sealed class ApproveObjectiveChangeRequestCommandHandlerIntegrationTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid ApproverUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ApproverEmployeeId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    private static readonly Guid RequesterEmployeeId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private static readonly Guid ProjectId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private static readonly Guid ParentObjectiveId = Guid.Parse("aaaaaaaa-0001-0001-0001-000000000001");
    private static readonly Guid ChildObjectiveId = Guid.Parse("aaaaaaaa-0002-0002-0002-000000000002");
    private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-0003-0003-0003-000000000003");

    private const decimal ParentAllocatedHours = 100m;
    private const decimal ChildAllocatedHours = 60m;
    private const decimal RequestedAdditionalHours = 20m;
    private const decimal ExpectedNewChildAllocatedHours = 80m;

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public ApproveObjectiveChangeRequestCommandHandlerIntegrationTests()
    {
        var databaseName = $"approve_ocr_integration_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Foreign Keys=False";

        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

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

        [Fact]
    public async Task Handle_ExtendAllocation_WithEditedAmountPersistsEditedAllocation()
    {
        await using var db = CreateContext();
        await SeedExtendAllocationScenarioAsync(db);

        var objectives = new EfObjectiveRepository(db);
        var changeRequests = new EfObjectiveChangeRequestRepository(db);
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(
                TenantId, ParentObjectiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(ApproverUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, ApproverUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApproverEmployeeId);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new ApproveObjectiveChangeRequestCommandHandler(
            currentUser.Object, identity.Object, changeRequests, objectives, membership.Object,
            new ObjectiveAllocationSlackCalculator(objectives, tasks.Object),
            new Mock<INotificationDispatcher>().Object, new UnitOfWork(db));

        var result = await handler.Handle(
            new ApproveObjectiveChangeRequestCommand(RequestId, ApprovedAdditionalHours: 12m),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updatedChild = await db.Objectives.AsNoTracking().SingleAsync(o => o.Id == ChildObjectiveId);
        Assert.Equal(ChildAllocatedHours + 12m, updatedChild.AllocatedHours);
    }

    [Fact]
    public async Task Handle_ExtendAllocation_DoesNotThrowDuplicateTrackingException()

    {
        await using var db = CreateContext();
        await SeedExtendAllocationScenarioAsync(db);

        var objectives = new EfObjectiveRepository(db);
        var changeRequests = new EfObjectiveChangeRequestRepository(db);
        var tasks = new Mock<IWorkTaskRepository>();
        tasks.Setup(x => x.GetActiveAllocationSumByObjectiveIdAsync(
                TenantId, ParentObjectiveId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(ApproverUserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, ApproverUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApproverEmployeeId);

        var membership = new Mock<IMilestoneMembershipCoordinator>();
        membership.Setup(x => x.GetActiveAssigneeAsync(TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.CoreHr.Entities.Employee?)null);

        var handler = new ApproveObjectiveChangeRequestCommandHandler(
            currentUser.Object,
            identity.Object,
            changeRequests,
            objectives,
            membership.Object,
            new ObjectiveAllocationSlackCalculator(objectives, tasks.Object),
            new Mock<INotificationDispatcher>().Object,
            new UnitOfWork(db));

        var result = await handler.Handle(new ApproveObjectiveChangeRequestCommand(RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updatedChild = await db.Objectives.AsNoTracking().SingleAsync(o => o.Id == ChildObjectiveId);
        Assert.Equal(ExpectedNewChildAllocatedHours, updatedChild.AllocatedHours);
    }

    private static async Task SeedExtendAllocationScenarioAsync(ApplicationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        db.Objectives.Add(new Objective
        {
            Id = ParentObjectiveId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            Title = "Approver own objective",
            OwnerId = ApproverEmployeeId,
            IsActive = true,
            AllocatedHours = ParentAllocatedHours,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            CreatedAt = now,
            CreatedById = ApproverUserId
        });

        db.Objectives.Add(new Objective
        {
            Id = ChildObjectiveId,
            TenantId = TenantId,
            ProjectId = ProjectId,
            ParentObjectiveId = ParentObjectiveId,
            Title = "Child being extended",
            OwnerId = RequesterEmployeeId,
            ReportingManagerId = ApproverEmployeeId,
            IsActive = true,
            AllocatedHours = ChildAllocatedHours,
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 11, 30),
            CreatedAt = now,
            CreatedById = ApproverUserId
        });

        db.ObjectiveChangeRequests.Add(new ObjectiveChangeRequest
        {
            Id = RequestId,
            TenantId = TenantId,
            ObjectiveId = ChildObjectiveId,
            RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
            RequestedById = RequesterEmployeeId,
            ReportingManagerId = ApproverEmployeeId,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = JsonSerializer.Serialize(new ExtendAllocationRequestPayload(RequestedAdditionalHours, "Need more hours")),
            CreatedAt = now,
            CreatedById = ApproverUserId
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
