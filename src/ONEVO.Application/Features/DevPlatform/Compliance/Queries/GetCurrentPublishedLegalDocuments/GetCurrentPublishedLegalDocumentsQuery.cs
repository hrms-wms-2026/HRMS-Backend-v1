using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;

/// <summary>Current published required dashboard-blocking documents (terms/privacy_notice today).</summary>
public sealed record GetCurrentPublishedLegalDocumentsQuery : IRequest<Result<IReadOnlyList<PublishedLegalDocumentDto>>>;

public sealed class GetCurrentPublishedLegalDocumentsQueryHandler
    : IRequestHandler<GetCurrentPublishedLegalDocumentsQuery, Result<IReadOnlyList<PublishedLegalDocumentDto>>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetCurrentPublishedLegalDocumentsQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<PublishedLegalDocumentDto>>> Handle(
        GetCurrentPublishedLegalDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        var entities = await _repository.GetCurrentRequiredVersionsAsync(cancellationToken);
        var dtos = entities.Select(LegalDocumentVersionMapper.ToPublishedDto).ToList();

        return Result<IReadOnlyList<PublishedLegalDocumentDto>>.Success(dtos);
    }
}
