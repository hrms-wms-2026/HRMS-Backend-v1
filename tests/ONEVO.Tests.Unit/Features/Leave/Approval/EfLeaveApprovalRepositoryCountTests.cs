using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Approval;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

/// <summary>Task 00a: the actionable count for requests created before policy-version pinning resolves every policy over
/// one snapshot read and gives the same answer as GetStateAsync + LeavePendingApprovals.IsActionable per request.</summary>
public sealed class EfLeaveApprovalRepositoryCountTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private readonly Mock<ILeavePolicyResolver> _policies = new();
    private readonly Dictionary<Guid, string?> _modeByEmployee = new();

    public EfLeaveApprovalRepositoryCountTests()
    {
        _policies.Setup(p => p.ResolveManyAsync(TenantId, It.IsAny<IReadOnlyCollection<LeavePolicyLookup>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<LeavePolicyLookup> lookups, CancellationToken _)
                => lookups.Distinct().ToDictionary(l => l, l => Resolved(l.EmployeeId, l.LeaveTypeId)));
        _policies.Setup(p => p.ResolveAsync(TenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid employeeId, Guid leaveTypeId, DateOnly _, CancellationToken _) => Resolved(employeeId, leaveTypeId));
    }

    private ResolvedLeavePolicy? Resolved(Guid employeeId, Guid leaveTypeId)
        => _modeByEmployee.GetValueOrDefault(employeeId) is { } mode
            ? new ResolvedLeavePolicy(leaveTypeId, Guid.NewGuid(), "Policy",
                new LeavePolicyVersion { Id = Guid.NewGuid(), TenantId = TenantId, ApprovalMode = mode }, [], "legal_entity", Guid.NewGuid(), "Acme")
            : null;

    [Fact]
    public async Task Legacy_requests_are_counted_like_the_per_request_state_check_in_batches()
    {
        await using var db = BuildDb();
        var annual = new LeaveType { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Annual Leave", Code = "AL" };
        db.LeaveTypes.Add(annual);
        Employee Person(string? mode)
        {
            var e = new Employee
            {
                Id = Guid.NewGuid(), TenantId = TenantId, UserId = Guid.NewGuid(), FirstName = "P", LastName = "Q",
                EmployeeNumber = Guid.NewGuid().ToString("N")[..8], HireDate = new DateOnly(2024, 1, 1),
            };
            _modeByEmployee[e.Id] = mode;
            db.Employees.Add(e);
            return e;
        }
        var requests = new List<LeaveRequest>();
        void Request(Employee employee, Guid leaveTypeId, params (Guid Approver, int Sequence)[] approvers)
        {
            var r = new LeaveRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee.Id, LeaveTypeId = leaveTypeId,
                StartAt = new DateTimeOffset(2026, 9, 14 + requests.Count, 9, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2026, 9, 14 + requests.Count, 18, 0, 0, TimeSpan.Zero),
                TotalHours = 8m, PaidHours = 8m, Status = LeaveRequestStatuses.Pending,
            };
            requests.Add(r);
            db.LeaveRequests.Add(r);
            foreach (var (approver, sequence) in approvers)
                db.LeaveRequestApprovers.Add(new LeaveRequestApprover
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = r.Id, ApproverEmployeeId = approver,
                    SequenceOrder = sequence, Status = LeaveRequestApproverStatuses.Pending,
                });
        }

        var inOrder = Person(LeaveApprovalModes.InOrder);
        var anyOne = Person(LeaveApprovalModes.AnyOne);
        var noPolicy = Person(null);
        Request(inOrder, annual.Id, (Me, 1), (Other, 2));     // my turn: counted
        Request(inOrder, annual.Id, (Other, 1), (Me, 2));     // waits for Other: not counted
        Request(anyOne, annual.Id, (Other, 1), (Me, 2));      // any one may decide: counted
        Request(noPolicy, annual.Id, (Me, 1));                // no policy resolves: not counted
        Request(anyOne, Guid.NewGuid(), (Me, 1));             // its leave type is gone: not counted
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new EfLeaveApprovalRepository(db, _policies.Object);
        var count = await repo.CountActionableAsync(TenantId, Me);

        count.Should().Be(2);
        _policies.Verify(p => p.ResolveManyAsync(TenantId, It.IsAny<IReadOnlyCollection<LeavePolicyLookup>>(), It.IsAny<CancellationToken>()), Times.Once);
        _policies.Verify(p => p.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        var perRequest = 0;
        foreach (var r in requests)
            if (await repo.GetStateAsync(TenantId, r.Id) is { } state && LeavePendingApprovals.IsActionable(state, Me))
                perRequest++;
        count.Should().Be(perRequest);
    }

    [Fact]
    public async Task No_legacy_requests_means_no_policy_resolution()
    {
        await using var db = BuildDb();

        (await new EfLeaveApprovalRepository(db, _policies.Object).CountActionableAsync(TenantId, Me)).Should().Be(0);

        _policies.Verify(p => p.ResolveManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<LeavePolicyLookup>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ApplicationDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, new Mock<IDateTimeProvider>().Object),
            new SoftDeleteInterceptor(new Mock<IDateTimeProvider>().Object),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
