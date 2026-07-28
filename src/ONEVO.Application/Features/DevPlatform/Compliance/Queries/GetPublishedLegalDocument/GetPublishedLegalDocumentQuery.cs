using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;

/// <summary>Public read by (document_type, version). Only ever returns status=published rows - never draft/archived.</summary>
public sealed record GetPublishedLegalDocumentQuery(string DocumentType, string Version)
    : IRequest<Result<PublishedLegalDocumentDto>>;

public sealed class GetPublishedLegalDocumentQueryHandler
    : IRequestHandler<GetPublishedLegalDocumentQuery, Result<PublishedLegalDocumentDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetPublishedLegalDocumentQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PublishedLegalDocumentDto>> Handle(
        GetPublishedLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetPublishedAsync(request.DocumentType, request.Version, cancellationToken);
        if (entity is null)
        {
            return Result<PublishedLegalDocumentDto>.NotFound(
                $"Published document '{request.DocumentType}' version '{request.Version}' was not found.");
        }

        return Result<PublishedLegalDocumentDto>.Success(LegalDocumentVersionMapper.ToPublishedDto(entity));
    }
}
