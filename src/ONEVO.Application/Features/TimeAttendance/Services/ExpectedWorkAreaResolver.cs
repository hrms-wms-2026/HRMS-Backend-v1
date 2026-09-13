using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public sealed class ExpectedWorkAreaResolver(
    IDateTimeProvider dateTime,
    IWorkModeRepository workModes,
    IWorkAreaChangeRequestRepository workAreaChangeRequests) : IExpectedWorkAreaResolver
{
    public const string SourceApprovedRequest = "approved_work_area_change_request";
    public const string SourceActiveWorkMode = "active_employee_work_mode";

    public async Task<Result<ExpectedWorkAreaResolution>> ResolveAsync(
        Employee employee,
        LegalEntity legalEntity,
        DateOnly date,
        CancellationToken ct = default)
    {
        var schedule = AttendanceScheduleResolver.ResolveForDate(legalEntity, date, dateTime.UtcNow);

        WorkAreaChangeRequest? approved;
        try
        {
            approved = await workAreaChangeRequests.GetApprovedForDateAsync(
                employee.TenantId, legalEntity.Id, employee.Id, date, ct);
        }
        catch (InconsistentWorkAreaChangeRequestStateException)
        {
            return Result<ExpectedWorkAreaResolution>.Conflict(
                "The expected work area could not be resolved because more than one approved override exists for this date.");
        }

        if (approved is not null)
        {
            return Result<ExpectedWorkAreaResolution>.Success(
                new ExpectedWorkAreaResolution(
                    approved.RequestedWorkModeId, approved.RequestedWorkModeName,
                    schedule.Timezone, SourceApprovedRequest));
        }

        if (employee.WorkModeId is not Guid workModeId)
            return Result<ExpectedWorkAreaResolution>.Conflict("The employee work mode is not configured.");

        var mode = await workModes.GetByIdAsync(employee.TenantId, workModeId, ct);
        if (mode is null)
            return Result<ExpectedWorkAreaResolution>.Conflict("The employee's assigned work mode was not found.");

        return Result<ExpectedWorkAreaResolution>.Success(
            new ExpectedWorkAreaResolution(mode.Id, mode.Name, schedule.Timezone, SourceActiveWorkMode));
    }
}
