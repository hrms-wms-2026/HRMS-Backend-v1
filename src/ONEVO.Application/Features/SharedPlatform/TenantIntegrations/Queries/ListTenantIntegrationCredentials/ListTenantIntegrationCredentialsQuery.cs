using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.ListTenantIntegrationCredentials;

public sealed record ListTenantIntegrationCredentialsQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<TenantIntegrationCredentialDto>>>;

public sealed class ListTenantIntegrationCredentialsQueryHandler
    : IRequestHandler<ListTenantIntegrationCredentialsQuery, Result<IReadOnlyList<TenantIntegrationCredentialDto>>>
{
    private readonly ITenantIntegrationCredentialRepository _repository;
    public ListTenantIntegrationCredentialsQueryHandler(ITenantIntegrationCredentialRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<TenantIntegrationCredentialDto>>> Handle(
        ListTenantIntegrationCredentialsQuery request, CancellationToken cancellationToken)
    {
        if (!await _repository.TenantExistsAsync(request.TenantId, cancellationToken))
        {
            return Result<IReadOnlyList<TenantIntegrationCredentialDto>>.NotFound("Tenant was not found.");
        }

        var values = await _repository.ListByTenantAsync(request.TenantId, cancellationToken);
        var dtos = values.Select(TenantIntegrationCredentialMapper.ToSafeDto).ToList();
        return Result<IReadOnlyList<TenantIntegrationCredentialDto>>.Success(dtos);
    }
}
