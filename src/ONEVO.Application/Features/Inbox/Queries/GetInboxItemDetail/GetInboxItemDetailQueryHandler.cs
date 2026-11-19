using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxItemDetail;

public sealed class GetInboxItemDetailQueryHandler : IRequestHandler<GetInboxItemDetailQuery, Result<InboxItemDetail>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxRouter _router;

    public GetInboxItemDetailQueryHandler(InboxCallerResolver caller, IInboxRouter router)
    {
        _caller = caller;
        _router = router;
    }

    public async Task<Result<InboxItemDetail>> Handle(GetInboxItemDetailQuery request, CancellationToken ct)
        => await _caller.ResolveAsync(ct) is { } c
            ? await _router.GetDetailAsync(c.TenantId, c.EmployeeId, request.Source, request.Kind, request.Id, ct)
            : Result<InboxItemDetail>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
}
