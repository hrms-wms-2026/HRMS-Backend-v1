using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetGitHubTenantApproval;

public sealed record GetGitHubTenantApprovalQuery
    : IRequest<Result<GitHubTenantApprovalDto>>;

public sealed class GetGitHubTenantApprovalQueryHandler
    : IRequestHandler<GetGitHubTenantApprovalQuery, Result<GitHubTenantApprovalDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantIntegrationCredentialRepository _repository;

    public GetGitHubTenantApprovalQueryHandler(
        ICurrentUser currentUser,
        ITenantIntegrationCredentialRepository repository)
    {
        _currentUser = currentUser;
        _repository = repository;
    }

    public async Task<Result<GitHubTenantApprovalDto>> Handle(
        GetGitHubTenantApprovalQuery request,
        CancellationToken cancellationToken)
    {
        var approval = await _repository.GetByTenantAndIntegrationAsync(
            _currentUser.TenantId,
            GitHubUserOAuthRules.IntegrationKey,
            cancellationToken);
        return Result<GitHubTenantApprovalDto>.Success(
            GitHubTenantApprovalMapper.ToDto(approval));
    }
}
