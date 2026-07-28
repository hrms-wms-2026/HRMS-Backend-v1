using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Context;

public interface IClockInContextResolver
{
    Task<Result<ResolvedClockInContext>> ResolveAsync(
        Guid agentId,
        CancellationToken ct);
}

