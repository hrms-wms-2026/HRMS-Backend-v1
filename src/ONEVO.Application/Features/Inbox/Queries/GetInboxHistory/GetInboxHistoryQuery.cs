using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxHistory;

/// <summary>The History tab: decisions the caller took, newest first.</summary>
public sealed record GetInboxHistoryQuery(InboxHistoryQuery Query) : IRequest<Result<InboxHistoryPage>>;

public sealed class GetInboxHistoryQueryHandler : IRequestHandler<GetInboxHistoryQuery, Result<InboxHistoryPage>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public GetInboxHistoryQueryHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxHistoryPage>> Handle(GetInboxHistoryQuery request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? Result<InboxHistoryPage>.Success(await _router.GetHistoryAsync(c.TenantId, c.EmployeeId, request.Query, ct))
            : Result<InboxHistoryPage>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
