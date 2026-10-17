using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxKinds;

/// <summary>Every registered (source, kind) and its capabilities. The frontend loads this once and renders from it.</summary>
public sealed record GetInboxKindsQuery : IRequest<Result<IReadOnlyList<InboxKindDescriptor>>>;

public sealed class GetInboxKindsQueryHandler : IRequestHandler<GetInboxKindsQuery, Result<IReadOnlyList<InboxKindDescriptor>>>
{
    private readonly IInboxRouter _router;

    public GetInboxKindsQueryHandler(IInboxRouter router) => _router = router;

    public Task<Result<IReadOnlyList<InboxKindDescriptor>>> Handle(GetInboxKindsQuery request, CancellationToken ct)
        => Task.FromResult(Result<IReadOnlyList<InboxKindDescriptor>>.Success(_router.GetKinds()));
}
