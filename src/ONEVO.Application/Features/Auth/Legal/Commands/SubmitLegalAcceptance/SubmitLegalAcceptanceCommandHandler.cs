using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;

public record LegalAcceptanceItemInput(string DocumentType, string Version, string Decision);

public record SubmitLegalAcceptanceCommand(
    IReadOnlyList<LegalAcceptanceItemInput> Acceptances,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<bool>>;

public class SubmitLegalAcceptanceCommandHandler : IRequestHandler<SubmitLegalAcceptanceCommand, Result<bool>>
{
    private readonly ILegalDocumentVersionRepository _versionRepository;
    private readonly ILegalAcceptanceRepository _acceptanceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SubmitLegalAcceptanceCommandHandler(
        ILegalDocumentVersionRepository versionRepository,
        ILegalAcceptanceRepository acceptanceRepository,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _versionRepository = versionRepository;
        _acceptanceRepository = acceptanceRepository;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<bool>> Handle(SubmitLegalAcceptanceCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
        {
            return Result<bool>.Failure("Tenant context is not resolved.", 400);
        }

        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return Result<bool>.Failure("User identity is not authenticated.", 401);
        }

        if (request.Acceptances == null || request.Acceptances.Count == 0)
        {
            return Result<bool>.Failure("No legal acceptance items provided.", 400);
        }

        var now = _clock.UtcNow;

        foreach (var item in request.Acceptances)
        {
            var validDecision = string.Equals(item.Decision, "accepted", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(item.Decision, "acknowledged", StringComparison.OrdinalIgnoreCase);

            if (!validDecision)
            {
                return Result<bool>.Failure($"Invalid decision '{item.Decision}' for document '{item.DocumentType}'.", 400);
            }

            var docVer = await _versionRepository.GetByDocumentTypeAndVersionAsync(item.DocumentType, item.Version, cancellationToken);
            if (docVer is null)
            {
                return Result<bool>.Failure($"Document type '{item.DocumentType}' version '{item.Version}' not found.", 404);
            }

            if (!string.Equals(docVer.Status, "published", StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure($"Document version '{item.Version}' is not published.", 400);
            }

            if (!docVer.IsRequired)
            {
                return Result<bool>.Failure($"Document version '{item.Version}' is not a required document.", 400);
            }

            if (!docVer.PublishedAt.HasValue)
            {
                return Result<bool>.Failure($"Document version '{item.Version}' has no effective publish date.", 400);
            }

            if (docVer.PublishedAt.Value > now)
            {
                return Result<bool>.Failure($"Document version '{item.Version}' is not yet effective.", 400);
            }

            var record = new LegalAcceptanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                DocumentType = docVer.DocumentType,
                DocumentVersion = docVer.Version,
                Decision = item.Decision.ToLowerInvariant(),
                Required = docVer.IsRequired,
                DecidedAt = now,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                Source = "web"
            };

            await _acceptanceRepository.AddAsync(record, cancellationToken);
        }

        await _acceptanceRepository.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
