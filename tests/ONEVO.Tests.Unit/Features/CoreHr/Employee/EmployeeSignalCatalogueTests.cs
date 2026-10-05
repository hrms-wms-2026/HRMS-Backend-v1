using FluentAssertions;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeSignalCatalogueTests
{
    private static readonly DateOnly F = new(2026, 9, 1), T = new(2026, 9, 30);

    private static EmployeeAttendanceOverviewResponse Att(int absent = 0, int missing = 0, int late = 0, int shortH = 0, int offDay = 0, int leaveWork = 0) =>
        new(F, T, 22, 10, late, missing, 0, Array.Empty<EmployeeAttendanceDay>(), absent, shortH, offDay, leaveWork);

    private static EmployeeAttendanceDisciplineResponse Disc(int early = 0, int obDays = 0, int obMin = 0, int? loc = null) =>
        new(F, T, 0, early, 0, obDays, obMin, loc is not null, loc);

    private static EmployeeWorkOverviewResponse Work(int overdue) => new(F, T, 6, 1, 2, overdue, 2, 17, null);

    private static EmployeeApprovalActivityResponse Appr(int pending) =>
        new(F, T, pending, 0, 0, pending, Array.Empty<EmployeeApprovalItem>());

    [Fact]
    public void AllQuiet_ReturnsEmpty() =>
        EmployeeSignalCatalogue.Build(new(Att(), Disc(), Work(0), Appr(0), new(0, 0))).Should().BeEmpty();

    [Fact]
    public void OrdersStrictlyByRank_NotSeverity()
    {
        var s = EmployeeSignalCatalogue.Build(new(Att(late: 2), Disc(obDays: 1, obMin: 25), Work(1), Appr(2), new(0, 3)));
        s.Select(x => x.Key).Should().Equal("over_break", "late_clock_ins", "overdue_tasks", "pending_approvals", "monitoring_exceptions");
    }

    [Fact]
    public void OverBreak_ValueIsMinutes_DenominatorIsDays()
    {
        var s = EmployeeSignalCatalogue.Build(new(null, Disc(obDays: 2, obMin: 40), null, null, null)).Single();
        (s.Value, s.Unit, s.Denominator).Should().Be((40, "minutes", 2));
    }

    [Fact]
    public void MissingSources_OmitTheirSignals()
    {
        var s = EmployeeSignalCatalogue.Build(new(Att(absent: 3), null, null, null, null));
        s.Select(x => x.Key).Should().Equal("absent_days");
    }

    [Fact]
    public void LocationViolations_NullMeansTrackingOff_Omitted() =>
        EmployeeSignalCatalogue.Build(new(null, Disc(loc: null), null, null, null)).Should().BeEmpty();

    [Fact]
    public void OffScheduleWork_SumsBothCounts() =>
        EmployeeSignalCatalogue.Build(new(Att(offDay: 1, leaveWork: 2), null, null, null, null)).Single().Value.Should().Be(3);

    [Fact]
    public void ExceptionsNotVisible_OmitsOnlyExceptionSignal()
    {
        var s = EmployeeSignalCatalogue.Build(new(null, null, null, null, new(4, null)));
        s.Select(x => x.Key).Should().Equal("idle_activity_alerts");
    }
}
