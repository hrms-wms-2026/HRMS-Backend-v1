using FluentAssertions;
using ONEVO.Application.Features.Leave.Calendar.Commands;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveHolidayCommandTests
{
    [Fact]
    public void Validator_RejectsEndBeforeStart()
    {
        var validator = new CreateLeaveHolidayCommandValidator();
        var result = validator.Validate(new CreateLeaveHolidayCommand(
            "Founders Day", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 9)));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_AcceptsSingleDay()
    {
        var validator = new CreateLeaveHolidayCommandValidator();
        var result = validator.Validate(new CreateLeaveHolidayCommand(
            "Founders Day", new DateOnly(2026, 9, 10), null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ToResponse_MapsExclusiveEndBackToInclusiveDate()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Title = "Founders Day",
            StartDate = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero)
        };

        var dto = ListLeaveHolidaysQueryHandler.ToResponse(calendarEvent);

        dto.Name.Should().Be("Founders Day");
        dto.Date.Should().Be(new DateOnly(2026, 9, 10));
        dto.EndDate.Should().Be(new DateOnly(2026, 9, 10));
    }

    [Fact]
    public void ToResponse_KeepsInclusiveRangeForMultiDayHoliday()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Title = "New Year",
            StartDate = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero)
        };

        var dto = ListLeaveHolidaysQueryHandler.ToResponse(calendarEvent);

        dto.Date.Should().Be(new DateOnly(2026, 12, 31));
        dto.EndDate.Should().Be(new DateOnly(2027, 1, 2));
    }
}
