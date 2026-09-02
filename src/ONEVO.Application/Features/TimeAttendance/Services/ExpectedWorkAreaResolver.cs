using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
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
            var requestedArea = approved.RequestedWorkArea.Trim().ToLowerInvariant();
            return requestedArea is WorkAreaChangeRequest.WorkAreaOnsite or WorkAreaChangeRequest.WorkAreaRemote
                ? Result<ExpectedWorkAreaResolution>.Success(
                    new ExpectedWorkAreaResolution(requestedArea, schedule.Timezone, SourceApprovedRequest))
                : Result<ExpectedWorkAreaResolution>.Conflict(
                    "The approved work-area change request has an unsupported requested work area.");
        }

        var mode = (await workModes.ListActiveAsync(ct))
            .FirstOrDefault(x => x.Id == employee.WorkModeId);
        var code = mode?.Code?.Trim().ToLowerInvariant();

        var expected = code switch
        {
            "onsite" or "on_site" => "onsite",
            "remote" => "remote",
            "hybrid" => "either",
            "field" => "field",
            _ => null
        };

        return expected is null
            ? Result<ExpectedWorkAreaResolution>.Conflict("The employee work mode is not configured.")
            : Result<ExpectedWorkAreaResolution>.Success(
                new ExpectedWorkAreaResolution(expected, schedule.Timezone, SourceActiveWorkMode));
    }
}
