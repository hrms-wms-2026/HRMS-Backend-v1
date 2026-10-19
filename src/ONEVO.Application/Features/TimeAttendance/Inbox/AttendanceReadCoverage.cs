using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>
/// HR scope for attendance items in the Inbox: attendance:read only reaches the employees in the reader's coverage
/// (the same visibility the attendance read endpoints use), never every attendance item of the tenant.
/// </summary>
public static class AttendanceReadCoverage
{
    public static async Task<bool> CoversAsync(
        IEmployeeAuthorityResolver authority, Guid readerUserId, Guid? legalEntityId, Guid employeeId, CancellationToken ct)
    {
        if (legalEntityId is not Guid le)
            return false;
        var visible = await authority.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            readerUserId, le, AttendanceInboxKinds.ReadPermission, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead), ct);
        return visible.EmployeeIds.Contains(employeeId);
    }
}
