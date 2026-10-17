using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Commands.ExecuteInboxBulkAction;

/// <summary>Runs one action over many items; each item succeeds or fails on its own.</summary>
public sealed record ExecuteInboxBulkActionCommand(InboxBulkRequest Request) : IRequest<Result<InboxBulkResult>>;

public sealed class ExecuteInboxBulkActionCommandHandler : IRequestHandler<ExecuteInboxBulkActionCommand, Result<InboxBulkResult>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public ExecuteInboxBulkActionCommandHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxBulkResult>> Handle(ExecuteInboxBulkActionCommand request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? await _router.ExecuteBulkAsync(c.TenantId, c.EmployeeId, request.Request, ct)
            : Result<InboxBulkResult>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
