using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands;

public sealed record CreateMeetingLinkCommand(Guid SeriesId, MeetingLinkRequest Request) : IRequest<Result<MeetingLinkResponse>>;

public sealed record RetryMeetingLinkCommand(Guid SeriesId) : IRequest<Result<MeetingLinkResponse>>;

public sealed record RemoveMeetingLinkCommand(Guid SeriesId) : IRequest<Result<MeetingLinkResponse?>>;

public sealed record ConfirmMeetingLinkCleanupCommand(Guid SeriesId) : IRequest<Result<bool>>;

/// <summary>The /meetings link writes (spec 5.7): resolve the caller's employee, then <see cref="SeriesMeetingLinkService"/>.</summary>
public sealed class MeetingLinkCommandHandlers :
    IRequestHandler<CreateMeetingLinkCommand, Result<MeetingLinkResponse>>,
    IRequestHandler<RetryMeetingLinkCommand, Result<MeetingLinkResponse>>,
    IRequestHandler<RemoveMeetingLinkCommand, Result<MeetingLinkResponse?>>,
    IRequestHandler<ConfirmMeetingLinkCleanupCommand, Result<bool>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly SeriesMeetingLinkService _links;

    public MeetingLinkCommandHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, SeriesMeetingLinkService links)
    {
        _currentUser = currentUser;
        _identity = identity;
        _links = links;
    }

    private Guid Tenant => _currentUser.TenantId;

    public async Task<Result<MeetingLinkResponse>> Handle(CreateMeetingLinkCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _links.RegenerateAsync(Tenant, caller, _currentUser.UserId, c.SeriesId, c.Request, ct)
            : MeetingReadService.NotFound<MeetingLinkResponse>();

    public async Task<Result<MeetingLinkResponse>> Handle(RetryMeetingLinkCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _links.RetryAsync(Tenant, caller, c.SeriesId, ct)
            : MeetingReadService.NotFound<MeetingLinkResponse>();

    public async Task<Result<MeetingLinkResponse?>> Handle(RemoveMeetingLinkCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _links.RemoveAsync(Tenant, caller, c.SeriesId, ct)
            : MeetingReadService.NotFound<MeetingLinkResponse?>();

    public async Task<Result<bool>> Handle(ConfirmMeetingLinkCleanupCommand c, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller ? await _links.ConfirmCleanupAsync(Tenant, caller, c.SeriesId, ct)
            : MeetingReadService.NotFound<bool>();

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
