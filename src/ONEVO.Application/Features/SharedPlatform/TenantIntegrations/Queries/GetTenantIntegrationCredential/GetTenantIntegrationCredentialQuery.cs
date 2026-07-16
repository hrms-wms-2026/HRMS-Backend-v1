using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetTenantIntegrationCredential;

public sealed record GetTenantIntegrationCredentialQuery(Guid Id) : IRequest<Result<TenantIntegrationCredentialDto>>;

public sealed class GetTenantIntegrationCredentialQueryHandler
    : IRequestHandler<GetTenantIntegrationCredentialQuery, Result<TenantIntegrationCredentialDto>>
{
    private readonly ITenantIntegrationCredentialRepository _repository;
    public GetTenantIntegrationCredentialQueryHandler(ITenantIntegrationCredentialRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<TenantIntegrationCredentialDto>> Handle(
        GetTenantIntegrationCredentialQuery request, CancellationToken cancellationToken)
    {
        var value = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (value is null)
        {
            return Result<TenantIntegrationCredentialDto>.NotFound("Tenant integration credential was not found.");
        }

        return Result<TenantIntegrationCredentialDto>.Success(
            TenantIntegrationCredentialMapper.ToSafeDto(value));
    }
}
