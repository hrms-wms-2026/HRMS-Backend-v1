using ONEVO.Application.Common.Models;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

public interface IExpectedWorkAreaResolver
{
    Task<Result<ExpectedWorkAreaResolution>> ResolveAsync(
        Employee employee,
        LegalEntity legalEntity,
        DateOnly date,
        CancellationToken ct = default);
}

public sealed record ExpectedWorkAreaResolution(
    string WorkArea,
    string Timezone,
    string Source);
