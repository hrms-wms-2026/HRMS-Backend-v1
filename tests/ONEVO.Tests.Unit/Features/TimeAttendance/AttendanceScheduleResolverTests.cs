using FluentAssertions;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Tests.Unit.Features.TimeAttendance;

public sealed class AttendanceScheduleResolverTests
{
    private static LegalEntity Entity(string? timezone, TimeOnly? start, TimeOnly? end) =>
        new() { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Timezone = timezone, WorkStartTime = start, WorkEndTime = end };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsScheduleConfigured_FalseWithoutTimezone(string? timezone) =>
        AttendanceScheduleResolver.IsScheduleConfigured(Entity(timezone, new(9, 0), new(17, 0))).Should().BeFalse();

    [Fact]
    public void IsScheduleConfigured_FalseWithoutStart() =>
        AttendanceScheduleResolver.IsScheduleConfigured(Entity("Asia/Colombo", null, new(17, 0))).Should().BeFalse();

    [Fact]
    public void IsScheduleConfigured_FalseWithoutEnd() =>
        AttendanceScheduleResolver.IsScheduleConfigured(Entity("Asia/Colombo", new(9, 0), null)).Should().BeFalse();

    [Theory]
    [InlineData(17, 9)]
    [InlineData(9, 9)]
    public void IsScheduleConfigured_FalseWhenStartNotBeforeEnd(int startHour, int endHour) =>
        AttendanceScheduleResolver.IsScheduleConfigured(Entity("Asia/Colombo", new(startHour, 0), new(endHour, 0))).Should().BeFalse();

    [Fact]
    public void IsScheduleConfigured_TrueForResolvableTimezoneAndValidHours() =>
        AttendanceScheduleResolver.IsScheduleConfigured(Entity("Asia/Colombo", new(9, 0), new(17, 0))).Should().BeTrue();
}
