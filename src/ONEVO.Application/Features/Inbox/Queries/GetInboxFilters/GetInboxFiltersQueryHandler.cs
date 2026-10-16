using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxFilters;

public sealed class GetInboxFiltersQueryHandler : IRequestHandler<GetInboxFiltersQuery, Result<IReadOnlyList<InboxFilterField>>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public GetInboxFiltersQueryHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<IReadOnlyList<InboxFilterField>>> Handle(GetInboxFiltersQuery request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? Result<IReadOnlyList<InboxFilterField>>.Success(await _router.GetFiltersAsync(c.TenantId, c.EmployeeId, ct))
            : Result<IReadOnlyList<InboxFilterField>>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
