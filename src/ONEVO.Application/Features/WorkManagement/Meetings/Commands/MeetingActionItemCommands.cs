using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands;

public sealed record UnlinkMeetingActionItemCommand(Guid OccurrenceTaskId, Guid ActionTaskId) : IRequest<Result<bool>>;

/// <summary>The /meetings action-item writes: resolve the caller's employee, then <see cref="MeetingActionItemService"/>.</summary>
public sealed class MeetingActionItemCommandHandlers : IRequestHandler<UnlinkMeetingActionItemCommand, Result<bool>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingActionItemService _actionItems;

    public MeetingActionItemCommandHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingActionItemService actionItems)
    {
        _currentUser = currentUser;
        _identity = identity;
        _actionItems = actionItems;
    }

    public async Task<Result<bool>> Handle(UnlinkMeetingActionItemCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _actionItems.UnlinkAsync(_currentUser.TenantId, caller, c.OccurrenceTaskId, c.ActionTaskId, ct)
            : MeetingReadService.NotFound<bool>();

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
