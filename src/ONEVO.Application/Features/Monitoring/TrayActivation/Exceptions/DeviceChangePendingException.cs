namespace ONEVO.Application.Features.Monitoring.TrayActivation.Exceptions;

/// <summary>Thrown by TrayEnrollmentService.IssueAsync when the enrolling device's
/// fingerprint doesn't match the employee's current active device. A DeviceChangeRequest
/// has already been created/updated by the time this is thrown - callers should not retry
/// IssueAsync, only surface the pending-approval state to the client.</summary>
public sealed class DeviceChangePendingException : Exception
{
    public DeviceChangePendingException()
        : base("A different device is already approved for this employee; a change request has been raised.")
    {
    }
}
