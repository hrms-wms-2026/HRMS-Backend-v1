using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Context;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetClockInContext;

public sealed class GetClockInContextQueryHandler
    : IRequestHandler<GetClockInContextQuery, Result<ResolvedClockInContext>>
{
    private readonly IClockInContextResolver _resolver;

    public GetClockInContextQueryHandler(IClockInContextResolver resolver)
    {
        _resolver = resolver;
    }

    public Task<Result<ResolvedClockInContext>> Handle(
        GetClockInContextQuery request,
        CancellationToken cancellationToken) =>
        _resolver.ResolveAsync(request.AgentId, cancellationToken);
}

