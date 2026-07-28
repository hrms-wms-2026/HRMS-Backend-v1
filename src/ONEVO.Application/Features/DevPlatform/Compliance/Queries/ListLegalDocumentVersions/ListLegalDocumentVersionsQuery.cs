using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;

/// <summary>Lightweight list - never includes content_json/content_html/content_text.</summary>
public sealed record ListLegalDocumentVersionsQuery(string? DocumentType, string? Status)
    : IRequest<Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>>;

public sealed class ListLegalDocumentVersionsQueryHandler
    : IRequestHandler<ListLegalDocumentVersionsQuery, Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public ListLegalDocumentVersionsQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>> Handle(
        ListLegalDocumentVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await _repository.ListAsync(request.DocumentType, request.Status, cancellationToken);
        var dtos = entities.Select(LegalDocumentVersionMapper.ToSummaryDto).ToList();

        return Result<IReadOnlyList<LegalDocumentVersionSummaryDto>>.Success(dtos);
    }
}
