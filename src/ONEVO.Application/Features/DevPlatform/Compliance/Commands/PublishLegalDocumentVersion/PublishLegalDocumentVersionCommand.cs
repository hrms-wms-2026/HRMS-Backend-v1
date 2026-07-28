using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;

/// <summary>
/// Publishes a draft. Because of the non-deferrable partial unique index on
/// (document_type) WHERE status='published', the prior published row for the same
/// document_type must be archived and flushed to the database BEFORE the new row is
/// marked published and flushed - two SaveChangesAsync calls inside one transaction,
/// never one. Getting this ordering wrong only surfaces against real Postgres, not the
/// in-memory EF provider used in unit tests.
/// </summary>
public sealed record PublishLegalDocumentVersionCommand(
    Guid Id,
    string? PublishReason,
    Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class PublishLegalDocumentVersionCommandHandler
    : IRequestHandler<PublishLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public PublishLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        PublishLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        if (!string.Equals(entity.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Legal document version '{request.Id}' is '{entity.Status}' and cannot be published; only draft versions can be published.",
                409);
        }

        if (string.IsNullOrWhiteSpace(entity.ContentHtml)
            || string.IsNullOrWhiteSpace(entity.ContentText)
            || string.IsNullOrWhiteSpace(entity.ContentJson))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                "Draft content must be non-empty before publishing.", 400);
        }

        var now = _clock.UtcNow;

        await _unitOfWork.ExecuteInTransactionAsync<bool>(async ct =>
        {
            var currentPublished = await _repository.GetCurrentPublishedByDocumentTypeAsync(
                entity.DocumentType, ct);
            if (currentPublished is not null && currentPublished.Id != entity.Id)
            {
                currentPublished.Status = "archived";
                currentPublished.UpdatedAt = now;
                await _unitOfWork.SaveChangesAsync(ct);
            }

            entity.Status = "published";
            entity.PublishedAt = now;
            entity.PublishedById = request.ActorPlatformUserId;
            entity.PublishReason = request.PublishReason;
            entity.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);

            return true;
        }, cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
