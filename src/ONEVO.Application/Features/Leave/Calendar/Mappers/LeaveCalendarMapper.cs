using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Helpers;
using ONEVO.Application.Features.Leave.Calendar.Options;
using ONEVO.Application.Features.Leave.Calendar.Services;

namespace ONEVO.Application.Features.Leave.Calendar.Mappers;

public static class LeaveCalendarMapper
{
    public static LeaveCalendarMonthResponse ToMonthResponse(
        LeaveCalendarMonthRange range,
        bool includeTentativeBlocks,
        IReadOnlyList<LeaveCalendarAbsenceInstance> absenceInstances,
        IReadOnlyList<LeaveCalendarHoliday> holidays,
        LeaveCalendarOptions options)
    {
        var absencesByDate = absenceInstances.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());
        var holidaysByDate = holidays.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());

        var days = range.Dates().Select(date =>
        {
            absencesByDate.TryGetValue(date, out var dayAbsences);
            holidaysByDate.TryGetValue(date, out var dayHolidays);

            return new LeaveCalendarDayResponse(
                date,
                date.DayOfWeek.ToString(),
                (dayAbsences ?? []).Select(instance => ToAbsence(instance, options)).ToList(),
                (dayHolidays ?? []).Select(ToHoliday).ToList());
        }).ToList();

        var isEmpty = days.All(day => day.Absences.Count == 0 && day.Holidays.Count == 0);
        return new LeaveCalendarMonthResponse(
            range.Year,
            range.Month,
            range.MonthStart,
            range.MonthEnd,
            includeTentativeBlocks,
            isEmpty,
            days);
    }

    private static LeaveCalendarAbsenceResponse ToAbsence(
        LeaveCalendarAbsenceInstance instance,
        LeaveCalendarOptions options)
    {
        var row = instance.Row;
        var request = row.Request;

        return new LeaveCalendarAbsenceResponse(
            request.Id,
            request.EmployeeId,
            row.EmployeeName,
            row.DepartmentId,
            row.DepartmentName,
            row.LegalEntityId,
            row.LegalEntityName,
            request.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            row.LeaveTypeCategory,
            options.ColorFor(row.LeaveTypeCategory),
            request.Status,
            instance.IsTentative,
            instance.IsPartialCancellationHistory,
            request.StartDate,
            request.EndDate,
            request.TotalDays,
            request.HalfDayPeriod);
    }

    private static LeaveCalendarHolidayResponse ToHoliday(LeaveCalendarHoliday holiday)
        => new(holiday.Date, holiday.Name, holiday.LegalEntityId, holiday.LegalEntityName, holiday.Source);
}
