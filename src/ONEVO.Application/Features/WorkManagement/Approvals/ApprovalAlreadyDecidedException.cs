namespace ONEVO.Application.Features.WorkManagement.Approvals;

/// <summary>Another decision took the request row first (meetings-as-tasks spec 5.2 "409 if it was already decided").
/// Thrown inside the decision transaction so everything it already wrote rolls back; the handler answers 409.</summary>
public sealed class ApprovalAlreadyDecidedException : Exception
{
    public ApprovalAlreadyDecidedException(Guid requestId)
        : base($"Approval request {requestId} was decided by someone else.") => RequestId = requestId;

    public Guid RequestId { get; }
}
