using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.Queries;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class GetLeaveCalendarQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsAuthRequired()
    {
        var harness = Harness.Create(new Mock<ICurrentUser>());

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(LeaveCalendarMessages.AuthRequired);
    }

    [Fact]
    public async Task Handle_WhenMissingCalendarPermission_ReturnsCalendarPermissionMessage()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var harness = Harness.Create(CurrentUser(tenantId, userId, "leave:read"));

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(LeaveCalendarMessages.CalendarPermissionRequired);
    }

    [Fact]
    public async Task Handle_WhenMissingLeaveScope_ReturnsLeaveScopeMessage()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read"));

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.StatusCode.Should().Be(403);
        result.Error.Should().Be(LeaveCalendarMessages.LeaveScopeRequired);
    }

    [Fact]
    public async Task Handle_WithOwnReadPermission_UsesOwnEmployeeScope()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = Employee(tenantId, userId);
        EmployeeVisibilityScope? capturedScope = null;
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read", "leave:read-own"));
        harness.Employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, EmployeeVisibilityScope, LeaveCalendarRequestFilter, CancellationToken>((_, scope, _, _) => capturedScope = scope)
            .ReturnsAsync([]);

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedScope.Should().NotBeNull();
        capturedScope!.CanViewAllTenantEmployees.Should().BeFalse();
        capturedScope.OwnEmployeeId.Should().Be(employee.Id);
    }

    [Fact]
    public async Task Handle_WithTeamReadPermission_UsesVisibilityResolver()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expectedScope = new EmployeeVisibilityScope(
            false,
            null,
            new HashSet<Guid>(),
            new HashSet<Guid> { Guid.NewGuid() },
            new HashSet<Guid>());
        EmployeeVisibilityScope? capturedScope = null;
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read", "leave:read-team"));
        harness.VisibilityScopes.Setup(x => x.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedScope);
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, EmployeeVisibilityScope, LeaveCalendarRequestFilter, CancellationToken>((_, scope, _, _) => capturedScope = scope)
            .ReturnsAsync([]);

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedScope.Should().Be(expectedScope);
        harness.VisibilityScopes.Verify(x => x.ResolveAsync(tenantId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("leave:read")]
    [InlineData("leave:manage")]
    public async Task Handle_WithAllReadPermission_UsesUnrestrictedScope(string permission)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        EmployeeVisibilityScope? capturedScope = null;
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read", permission));
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, EmployeeVisibilityScope, LeaveCalendarRequestFilter, CancellationToken>((_, scope, _, _) => capturedScope = scope)
            .ReturnsAsync([]);

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedScope!.CanViewAllTenantEmployees.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenIncludeTentativeIsNull_UsesConfiguredDefault()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        LeaveCalendarRequestFilter? capturedFilter = null;
        var harness = Harness.Create(
            CurrentUser(tenantId, userId, "calendar:read", "leave:read"),
            new LeaveCalendarOptions { DefaultIncludeTentativeBlocks = true });
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, EmployeeVisibilityScope, LeaveCalendarRequestFilter, CancellationToken>((_, _, filter, _) => capturedFilter = filter)
            .ReturnsAsync([]);

        await harness.Handler.Handle(Query(includeTentative: null), CancellationToken.None);

        capturedFilter!.IncludeTentativeBlocks.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenIncludeTentativeIsFalse_OverridesConfiguredDefault()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        LeaveCalendarRequestFilter? capturedFilter = null;
        var harness = Harness.Create(
            CurrentUser(tenantId, userId, "calendar:read", "leave:read"),
            new LeaveCalendarOptions { DefaultIncludeTentativeBlocks = true });
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, EmployeeVisibilityScope, LeaveCalendarRequestFilter, CancellationToken>((_, _, filter, _) => capturedFilter = filter)
            .ReturnsAsync([]);

        await harness.Handler.Handle(Query(includeTentative: false), CancellationToken.None);

        capturedFilter!.IncludeTentativeBlocks.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PassesVisibleLegalEntityIdsToHolidayProvider()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        IReadOnlyCollection<Guid>? capturedLegalEntityIds = null;
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read", "leave:read"));
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(tenantId, legalEntityId)]);
        harness.Holidays.Setup(x => x.ListHolidaysAsync(
                tenantId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyCollection<Guid>, DateOnly, DateOnly, CancellationToken>((_, ids, _, _, _) => capturedLegalEntityIds = ids)
            .ReturnsAsync([]);

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedLegalEntityIds.Should().BeEquivalentTo([legalEntityId]);
    }

    [Fact]
    public async Task Handle_ReturnsEveryDayInRequestedMonth()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var harness = Harness.Create(CurrentUser(tenantId, userId, "calendar:read", "leave:read"));
        harness.Repository.Setup(x => x.ListMonthRequestsAsync(
                tenantId,
                It.IsAny<EmployeeVisibilityScope>(),
                It.IsAny<LeaveCalendarRequestFilter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await harness.Handler.Handle(Query(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Days.Should().HaveCount(31);
    }

    private static GetLeaveCalendarQuery Query(bool? includeTentative = true) =>
        new(2026, 8, null, includeTentative);

    private static Mock<ICurrentUser> CurrentUser(Guid tenantId, Guid userId, params string[] permissions)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        currentUser.Setup(x => x.HasPermission(It.IsAny<string>()))
            .Returns<string>(permissions.Contains);
        return currentUser;
    }

    private static Employee Employee(Guid tenantId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        EmployeeNumber = "EMP-001",
        FirstName = "Priya",
        LastName = "Nair",
        HireDate = new DateOnly(2024, 1, 1)
    };

    private static LeaveCalendarRequestRow Row(Guid tenantId, Guid legalEntityId)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 8, 10),
            EndDate = new DateOnly(2026, 8, 10),
            Status = LeaveRequestStatuses.Approved,
            TotalDays = 1m,
            PaidDays = 1m
        };

        return new LeaveCalendarRequestRow(
            request,
            "Priya Nair",
            null,
            null,
            legalEntityId,
            "Acme Lanka",
            "Annual Leave",
            "AL",
            LeaveTypeCategories.Annual);
    }

    private sealed class Harness
    {
        private Harness(
            GetLeaveCalendarQueryHandler handler,
            Mock<IEmployeeRepository> employees,
            Mock<IEmployeeVisibilityScopeResolver> visibilityScopes,
            Mock<ILeaveCalendarRepository> repository,
            Mock<ILeaveCalendarHolidayProvider> holidays)
        {
            Handler = handler;
            Employees = employees;
            VisibilityScopes = visibilityScopes;
            Repository = repository;
            Holidays = holidays;
        }

        public GetLeaveCalendarQueryHandler Handler { get; }
        public Mock<IEmployeeRepository> Employees { get; }
        public Mock<IEmployeeVisibilityScopeResolver> VisibilityScopes { get; }
        public Mock<ILeaveCalendarRepository> Repository { get; }
        public Mock<ILeaveCalendarHolidayProvider> Holidays { get; }

        public static Harness Create(Mock<ICurrentUser> currentUser, LeaveCalendarOptions? options = null)
        {
            var employees = new Mock<IEmployeeRepository>();
            var visibilityScopes = new Mock<IEmployeeVisibilityScopeResolver>();
            var repository = new Mock<ILeaveCalendarRepository>();
            var holidays = new Mock<ILeaveCalendarHolidayProvider>();
            holidays.Setup(x => x.ListHolidaysAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var handler = new GetLeaveCalendarQueryHandler(
                currentUser.Object,
                employees.Object,
                visibilityScopes.Object,
                repository.Object,
                holidays.Object,
                new LeaveCalendarRequestProjector(),
                Options.Create(options ?? new LeaveCalendarOptions()));

            return new Harness(handler, employees, visibilityScopes, repository, holidays);
        }
    }
}
