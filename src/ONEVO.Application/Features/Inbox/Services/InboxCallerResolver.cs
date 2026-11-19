using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.Inbox.Services;

public sealed record InboxCaller(Guid TenantId, Guid EmployeeId);

/// <summary>The inbox is addressed by EmployeeId; resolves the session's user to it (null = no employee record).</summary>
public sealed class InboxCallerResolver
{
    public const string NoEmployeeMessage = "No employee record for the current user.";

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;

    public InboxCallerResolver(ICurrentUser currentUser, ICallerIdentityResolver identity)
    {
        _currentUser = currentUser;
        _identity = identity;
    }

    public async Task<InboxCaller?> ResolveAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return null;
        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        return employeeId is null ? null : new InboxCaller(_currentUser.TenantId, employeeId.Value);
    }
}
