using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Tests.Unit.Features.Leave;

public sealed class GetEmployeeTimeOffQueryHandlerTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<ILeaveEntitlementRepository> _entitlements = new();
    private readonly Mock<ILeavePolicyRepository> _policies = new();
    private readonly Mock<ILeaveRequestReadRepository> _leaveRequests = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T06:00:00+00:00");
    private static readonly DateOnly Today = new(2026, 8, 21);

    public GetEmployeeTimeOffQueryHandlerTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _user.Setup(u => u.HasPermission("leave:read")).Returns(true);
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _clock.SetupGet(c => c.Today).Returns(Today);
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _entitlements.Setup(e => e.ListRowsAsync(_tenantId, It.IsAny<LeaveEntitlementListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _leaveRequests.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<LeaveRequest>());
    }

    private GetEmployeeTimeOffQueryHandler CreateHandler() =>
        new(_guard.Object, _entitlements.Object, _policies.Object, _leaveRequests.Object, _user.Object, _clock.Object);

    private LeaveRequest Leave(string startUtc, string endUtc, decimal hours, Guid? typeId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId, LeaveTypeId = typeId ?? Guid.NewGuid(),
        StartAt = DateTimeOffset.Parse(startUtc), EndAt = DateTimeOffset.Parse(endUtc), TotalHours = hours
    };

    [Fact]
    public async Task Handle_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.NotFound("nope"));

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_DoesNotRequireAnyModulePermission()
    {
        _user.Setup(u => u.HasPermission("leave:read")).Returns(false);

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Handle_Returns400_ForAnImplausibleYear(int year)
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, year), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DefaultsToTheCurrentYear_AndQueriesOnlyThatEmployee()
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.Value!.Year.Should().Be(2026);
        _entitlements.Verify(e => e.ListRowsAsync(
            _tenantId,
            It.Is<LeaveEntitlementListFilter>(f => f.Year == 2026 && f.EmployeeId == _employeeId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PicksTheEarliestNotYetEndedApprovedLeave_AsNextLeave()
    {
        var earlier = Leave("2026-08-10T00:00:00+00:00", "2026-08-12T23:59:59+00:00", 24);   // already ended
        var next = Leave("2026-09-23T00:00:00+00:00", "2026-09-25T23:59:59+00:00", 24);
        var later = Leave("2026-10-05T00:00:00+00:00", "2026-10-06T23:59:59+00:00", 16);
        _leaveRequests.Setup(l => l.ListApprovedCoveringAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), Today, Today.AddDays(90), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { later, earlier, next });

        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        var leave = result.Value!.NextLeave!;
        leave.StartDate.Should().Be(new DateOnly(2026, 9, 23));
        leave.EndDate.Should().Be(new DateOnly(2026, 9, 25));
        leave.TotalHours.Should().Be(24);
        leave.LeaveTypeId.Should().Be(next.LeaveTypeId);
    }

    [Fact]
    public async Task Handle_NextLeaveIsNull_WhenNothingIsUpcoming()
    {
        var result = await CreateHandler().Handle(new GetEmployeeTimeOffQuery(_employeeId, null), CancellationToken.None);

        result.Value!.NextLeave.Should().BeNull();
        result.Value.Balances.Should().BeEmpty();
    }
}
