using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetMyEffectiveTimezone;

public sealed record MyEffectiveTimezoneResponse(string Timezone);

public sealed record GetMyEffectiveTimezoneQuery : IRequest<Result<MyEffectiveTimezoneResponse>>;
