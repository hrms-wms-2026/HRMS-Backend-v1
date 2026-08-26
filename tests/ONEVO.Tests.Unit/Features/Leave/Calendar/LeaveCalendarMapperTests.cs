using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Mappers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarMapperTests
{
    [Fact]
    public void ToMonthResponse_ReturnsAllDaysAndConfiguredColor()
    {
        var range = LeaveCalendarMonthRange.From(2026, 8).Value!;
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 8, 10),
            EndDate = new DateOnly(2026, 8, 10),
            Status = LeaveRequestStatuses.Approved,
            TotalDays = 1m,
            PaidDays = 1m
        };
        var row = new LeaveCalendarRequestRow(
            request, "Priya Nair", null, null, null, null, "Annual Leave", "AL", LeaveTypeCategories.Annual);
        var options = new LeaveCalendarOptions
        {
            TypeCategoryColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [LeaveTypeCategories.Annual] = "#2563EB"
            }
        };

        var response = LeaveCalendarMapper.ToMonthResponse(
            range,
            includeTentativeBlocks: true,
            [new LeaveCalendarAbsenceInstance(new DateOnly(2026, 8, 10), row, false, false)],
            [new LeaveCalendarHoliday(new DateOnly(2026, 8, 15), "Public holiday", null, null, "test")],
            options);

        response.Days.Should().HaveCount(31);
        response.Days.Single(x => x.Date == new DateOnly(2026, 8, 10))
            .Absences.Single().TypeColorHex.Should().Be("#2563EB");
        response.Days.Single(x => x.Date == new DateOnly(2026, 8, 15))
            .Holidays.Single().Name.Should().Be("Public holiday");
        response.IsEmpty.Should().BeFalse();
    }
}
