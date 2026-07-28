using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Compliance.Mappers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;

public sealed record GetLegalDocumentVersionQuery(Guid Id) : IRequest<Result<LegalDocumentVersionDetailDto>>;

public sealed class GetLegalDocumentVersionQueryHandler
    : IRequestHandler<GetLegalDocumentVersionQuery, Result<LegalDocumentVersionDetailDto>>
{
    private readonly ILegalDocumentVersionRepository _repository;

    public GetLegalDocumentVersionQueryHandler(ILegalDocumentVersionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<LegalDocumentVersionDetailDto>> Handle(
        GetLegalDocumentVersionQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
        {
            return Result<LegalDocumentVersionDetailDto>.NotFound(
                $"Legal document version '{request.Id}' was not found.");
        }

        return Result<LegalDocumentVersionDetailDto>.Success(LegalDocumentVersionMapper.ToDetailDto(entity));
    }
}
