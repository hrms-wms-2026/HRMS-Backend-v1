using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;

public sealed class ExecuteInboxActionCommandHandler : IRequestHandler<ExecuteInboxActionCommand, Result<InboxActionResult>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public ExecuteInboxActionCommandHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxActionResult>> Handle(ExecuteInboxActionCommand request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? await _router.ExecuteActionAsync(c.TenantId, c.EmployeeId, request.Source, request.Kind, request.Id, request.Action, request.Body, ct)
            : Result<InboxActionResult>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
