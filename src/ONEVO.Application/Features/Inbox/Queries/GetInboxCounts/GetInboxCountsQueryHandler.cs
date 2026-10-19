using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxCounts;

public sealed class GetInboxCountsQueryHandler : IRequestHandler<GetInboxCountsQuery, Result<InboxCounts>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public GetInboxCountsQueryHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxCounts>> Handle(GetInboxCountsQuery request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? Result<InboxCounts>.Success(await _router.GetCountsAsync(c.TenantId, c.EmployeeId, ct))
            : Result<InboxCounts>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
