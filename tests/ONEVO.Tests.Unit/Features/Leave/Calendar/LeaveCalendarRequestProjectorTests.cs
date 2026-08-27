using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarRequestProjectorTests
{
    private readonly LeaveCalendarRequestProjector _projector = new();

    [Fact]
    public void Project_ExpandsApprovedRequestAcrossVisibleDates()
    {
        var row = Row(LeaveRequestStatuses.Approved, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11),
            new DateOnly(2026, 8, 12));
        result.Should().OnlyContain(x => !x.IsTentative);
    }

    [Fact]
    public void Project_ClipsRequestToMonthRange()
    {
        var row = Row(LeaveRequestStatuses.Approved, new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 2));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 2));
    }

    [Fact]
    public void Project_IncludesPendingAsTentative_WhenConfigured()
    {
        var row = Row(LeaveRequestStatuses.Pending, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Should().ContainSingle(x => x.IsTentative);
    }

    [Fact]
    public void Project_ExcludesPending_WhenTentativeBlocksAreDisabled()
    {
        var row = Row(LeaveRequestStatuses.Pending, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Project_ExcludesRejectedRequests()
    {
        var row = Row(LeaveRequestStatuses.Rejected, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 10));

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Project_ForPartialCancellation_ShowsOnlyHistoricalTakenPortion()
    {
        var row = Row(LeaveRequestStatuses.Cancelled, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));
        row.Request.PartialCancelEffectiveDate = new DateOnly(2026, 8, 13);

        var result = _projector.Project([row], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), includeTentativeBlocks: true);

        result.Select(x => x.Date).Should().Equal(
            new DateOnly(2026, 8, 10),
            new DateOnly(2026, 8, 11),
            new DateOnly(2026, 8, 12));
        result.Should().OnlyContain(x => x.IsPartialCancellationHistory);
    }

    private static LeaveCalendarRequestRow Row(string status, DateOnly start, DateOnly end)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            StartDate = start,
            EndDate = end,
            Status = status,
            TotalDays = end.DayNumber - start.DayNumber + 1,
            PaidDays = end.DayNumber - start.DayNumber + 1
        };

        return new LeaveCalendarRequestRow(
            request,
            "Priya Nair",
            Guid.NewGuid(),
            "Engineering",
            Guid.NewGuid(),
            "Acme Lanka",
            "Annual Leave",
            "AL",
            LeaveTypeCategories.Annual);
    }
}
