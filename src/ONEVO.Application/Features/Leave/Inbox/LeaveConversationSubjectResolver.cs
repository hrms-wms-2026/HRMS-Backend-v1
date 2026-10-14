using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>
/// The chat of a leave request: the employee, whoever submitted it on their behalf, and every approver row (including
/// a delegate's original approver), resolved now. HR with leave:read may view the request and its chat from their own
/// scope, and take the investigation export.
/// </summary>
public sealed class LeaveConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly ILeaveApprovalRepository _approvals;
    private readonly IEmployeeRepository _employees;
    private readonly IPermissionRepository _permissions;
    private readonly IDateTimeProvider _clock;

    public LeaveConversationSubjectResolver(
        ILeaveApprovalRepository approvals, IEmployeeRepository employees, IPermissionRepository permissions, IDateTimeProvider clock)
    {
        _approvals = approvals;
        _employees = employees;
        _permissions = permissions;
        _clock = clock;
    }

    public string Source => LeaveInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        if (subject.Kind != LeaveInboxKinds.Request)
            return null;
        var state = await _approvals.GetStateAsync(tenantId, subject.Id, ct);
        if (state is null)
            return null;

        var people = new List<Guid> { state.Request.EmployeeId };
        if (state.Request.SubmittedOnBehalfOfBy is { } submitterUserId
            && await _employees.GetByUserIdAsync(tenantId, submitterUserId, ct) is { } submitter)
            people.Add(submitter.Id);
        foreach (var row in state.Approvers)
        {
            people.Add(row.ApproverEmployeeId);
            if (row.DelegatedFromApproverId is { } original)
                people.Add(original);
        }
        var title = $"{state.LeaveTypeName} - {state.Employee.FirstName} {state.Employee.LastName}".Trim();
        return new ConversationSubject(people.Distinct().ToList(), title, null);
    }

    public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => HasLeaveReadAsync(tenantId, employeeId, ct);

    public Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => HasLeaveReadAsync(tenantId, employeeId, ct);

    private async Task<bool> HasLeaveReadAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee is null || employee.UserId == Guid.Empty)
            return false;
        var readers = await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, LeaveInboxSourceProvider.LeaveReadPermission, _clock.UtcNow, ct);
        return readers.Contains(employee.UserId);
    }
}
