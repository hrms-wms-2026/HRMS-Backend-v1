using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;

namespace ONEVO.Application.Features.Auth.Legal.Services;

public interface ILegalAcceptanceSubmissionService
{
    Task<Result<bool>> ValidateAndStageAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyList<LegalAcceptanceItemInput> acceptances,
        bool requireComplete,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
