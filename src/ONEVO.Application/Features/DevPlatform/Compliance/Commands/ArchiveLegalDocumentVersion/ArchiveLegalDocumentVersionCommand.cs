using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;

/// <summary>Archives a published version. Only status/updated_at change - content body is never touched.</summary>
public sealed record ArchiveLegalDocumentVersionCommand(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class ArchiveLegalDocumentVersionCommandHandler
    : IRequestHandler<ArchiveLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ArchiveLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        ArchiveLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        if (!string.Equals(entity.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Legal document version '{request.Id}' is '{entity.Status}'; only published versions can be archived.",
                409);
        }

        entity.Status = "archived";
        entity.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
