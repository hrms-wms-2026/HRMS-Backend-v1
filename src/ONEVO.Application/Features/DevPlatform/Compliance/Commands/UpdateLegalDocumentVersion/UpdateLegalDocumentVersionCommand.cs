using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;

/// <summary>
/// Edits a draft in place. document_type/version are immutable after create (not accepted
/// here). Published/archived versions reject with 409 - create a new draft instead.
/// </summary>
public sealed record UpdateLegalDocumentVersionCommand(
    Guid Id,
    string Title,
    string ContentJson,
    string ContentHtml,
    string ContentText,
    bool IsRequired,
    string BlockScope) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class UpdateLegalDocumentVersionCommandHandler
    : IRequestHandler<UpdateLegalDocumentVersionCommand, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateLegalDocumentVersionCommandHandler(
        ILegalDocumentVersionRepository repository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        UpdateLegalDocumentVersionCommand request,
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
                $"Legal document version '{request.Id}' is '{entity.Status}' and can no longer be edited. Create a new draft version instead.",
                409);
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

        entity.Title = request.Title.Trim();
        entity.ContentJson = request.ContentJson;
        entity.ContentHtml = request.ContentHtml;
        entity.ContentText = request.ContentText;
        entity.ContentHash = LegalContentHasher.ComputeHash(request.ContentHtml);
        entity.IsRequired = request.IsRequired;
        entity.BlockScope = request.BlockScope;
        entity.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
