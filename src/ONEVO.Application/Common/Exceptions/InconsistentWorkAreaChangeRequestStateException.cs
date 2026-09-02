namespace ONEVO.Application.Common.Exceptions;

/// <summary>
/// Application-layer signal that more than one approved work-area change request was found for
/// the same tenant/employee/date, which the partial unique index on
/// <c>work_area_change_requests</c> should already prevent. Thrown by repository implementations
/// instead of letting the resolver arbitrarily pick one row.
/// </summary>
public class InconsistentWorkAreaChangeRequestStateException : Exception
{
    public InconsistentWorkAreaChangeRequestStateException()
        : base("More than one approved work-area change request exists for this employee and date.")
    {
    }
}
