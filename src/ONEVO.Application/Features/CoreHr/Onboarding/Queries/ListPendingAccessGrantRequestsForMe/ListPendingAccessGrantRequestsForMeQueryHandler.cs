using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;

public class ListPendingAccessGrantRequestsForMeQueryHandler
    : IRequestHandler<ListPendingAccessGrantRequestsForMeQuery, Result<IReadOnlyList<PendingAccessGrantRequestResponse>>>
{
    private readonly IAccessGrantRequestRepository _repository;
    private readonly ICurrentUser _currentUser;

    public ListPendingAccessGrantRequestsForMeQueryHandler(
        IAccessGrantRequestRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PendingAccessGrantRequestResponse>>> Handle(
        ListPendingAccessGrantRequestsForMeQuery request, CancellationToken ct)
    {
        var items = await _repository.ListPendingAsync(_currentUser.TenantId, ct);
        return Result<IReadOnlyList<PendingAccessGrantRequestResponse>>.Success(items);
    }
}
