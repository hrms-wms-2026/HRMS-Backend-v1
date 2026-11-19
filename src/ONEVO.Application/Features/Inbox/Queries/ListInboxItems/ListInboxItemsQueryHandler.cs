using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.ListInboxItems;

public sealed class ListInboxItemsQueryHandler : IRequestHandler<ListInboxItemsQuery, Result<InboxPage>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public ListInboxItemsQueryHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxPage>> Handle(ListInboxItemsQuery request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? Result<InboxPage>.Success(await _router.ListAsync(c.TenantId, c.EmployeeId, request.Query, ct))
            : Result<InboxPage>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
