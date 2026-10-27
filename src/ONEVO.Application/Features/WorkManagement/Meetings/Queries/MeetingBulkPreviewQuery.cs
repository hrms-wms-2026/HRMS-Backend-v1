using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

/// <summary>POST /api/v1/meetings/bulk-preview (spec 5.2, D8). Expands the picked groups into a snapshot
/// of individual people, with the counts. The gate is the same as the meeting's own.</summary>
public sealed record MeetingBulkPreviewQuery(MeetingBulkPreviewRequest Request) : IRequest<Result<MeetingBulkPreviewResponse>>;

/// <summary>The bulk preview read: resolve the caller's employee, then <see cref="MeetingBulkInviteService"/>.</summary>
public sealed class MeetingBulkPreviewQueryHandlers : IRequestHandler<MeetingBulkPreviewQuery, Result<MeetingBulkPreviewResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingBulkInviteService _bulk;

    public MeetingBulkPreviewQueryHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingBulkInviteService bulk)
    {
        _currentUser = currentUser;
        _identity = identity;
        _bulk = bulk;
    }

    public async Task<Result<MeetingBulkPreviewResponse>> Handle(MeetingBulkPreviewQuery q, CancellationToken ct)
        => await CallerAsync(ct) is Guid caller
            ? await _bulk.PreviewAsync(_currentUser.TenantId, caller, q.Request, ct)
            : Result<MeetingBulkPreviewResponse>.Forbidden("No employee record for the current user.");

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
