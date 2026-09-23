using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;

public sealed class GetMyEffectiveTimezoneQueryHandler(ICurrentUser currentUser, ICalendarTimezoneResolver resolver)
    : IRequestHandler<GetMyEffectiveTimezoneQuery, Result<MyEffectiveTimezoneResponse>>
{
    public async Task<Result<MyEffectiveTimezoneResponse>> Handle(GetMyEffectiveTimezoneQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<MyEffectiveTimezoneResponse>.Forbidden();

        var timezone = await resolver.ResolveForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        return Result<MyEffectiveTimezoneResponse>.Success(new MyEffectiveTimezoneResponse(timezone));
    }
}
