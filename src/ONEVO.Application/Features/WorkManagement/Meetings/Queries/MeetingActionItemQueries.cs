using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

public sealed record GetMeetingActionItemsQuery(Guid OccurrenceTaskId) : IRequest<Result<IReadOnlyList<MeetingActionItemView>>>;

/// <summary>The /meetings action-item reads: resolve the caller's employee, then <see cref="MeetingActionItemService"/>.</summary>
public sealed class MeetingActionItemQueryHandlers : IRequestHandler<GetMeetingActionItemsQuery, Result<IReadOnlyList<MeetingActionItemView>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingActionItemService _actionItems;

    public MeetingActionItemQueryHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingActionItemService actionItems)
    {
        _currentUser = currentUser;
        _identity = identity;
        _actionItems = actionItems;
    }

    public async Task<Result<IReadOnlyList<MeetingActionItemView>>> Handle(GetMeetingActionItemsQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _actionItems.ListAsync(_currentUser.TenantId, caller, _currentUser.UserId, q.OccurrenceTaskId, ct)
            : MeetingReadService.NotFound<IReadOnlyList<MeetingActionItemView>>();

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
