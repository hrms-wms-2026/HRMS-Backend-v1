using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;

/// <summary>
/// Creates a draft legal document version. Never publishes automatically. content_hash is
/// always recomputed server-side from content_html - it is never accepted from the caller.
/// </summary>
public sealed record CreateLegalDocumentVersionCommand(
    string DocumentType,
    string Version,
    string Title,
    string ContentJson,
    string ContentHtml,
    string ContentText,
    bool IsRequired,
    string BlockScope,
    Guid ActorPlatformUserId) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class CreateLegalDocumentVersionCommandHandler
    : IRequestHandler<CreateLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        CreateLegalDocumentVersionCommand request,
        CancellationToken cancellationToken)
    {
        if (!LegalDocumentTypes.Allowed.Contains(request.DocumentType))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Document type '{request.DocumentType}' is not a supported Phase 1 legal document type.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("version is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("title is required.", 400);
        }

        if (!LegalDocumentBlockScopes.Allowed.Contains(request.BlockScope))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(
                $"Block scope '{request.BlockScope}' is not a supported Phase 1 block scope.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.ContentText))
        {
            return Result<LegalDocumentVersionDetailDto>.Failure("content_text must not be empty.", 400);
        }

        var contentJsonValidation = LegalContentJsonValidator.Validate(request.ContentJson);
        if (contentJsonValidation is not null)
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(contentJsonValidation, 400);
        }

        var validation = LegalHtmlValidator.Validate(request.ContentHtml);
        if (!validation.IsValid)
        {
            return Result<LegalDocumentVersionDetailDto>.Failure(validation.ErrorMessage!, 400);
        }

        var existing = await _repository.GetByDocumentTypeAndVersionAsync(
            request.DocumentType, request.Version, cancellationToken);
        if (existing is not null)
        {
            return Result<LegalDocumentVersionDetailDto>.Conflict(
                $"Version '{request.Version}' already exists for document type '{request.DocumentType}'.");
        }

        var now = _clock.UtcNow;
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentType = request.DocumentType,
            Version = request.Version,
            Title = request.Title.Trim(),
            ContentJson = request.ContentJson,
            ContentHtml = request.ContentHtml,
            ContentText = request.ContentText,
            ContentHash = LegalContentHasher.ComputeHash(request.ContentHtml),
            IsRequired = request.IsRequired,
            BlockScope = request.BlockScope,
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
