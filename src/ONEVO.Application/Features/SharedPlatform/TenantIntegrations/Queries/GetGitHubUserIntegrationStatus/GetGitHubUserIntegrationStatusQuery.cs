using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Mappers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetGitHubUserIntegrationStatus;

public sealed record GetGitHubUserIntegrationStatusQuery
    : IRequest<Result<UserIntegrationConnectionDto>>;

public sealed class GetGitHubUserIntegrationStatusQueryHandler
    : IRequestHandler<GetGitHubUserIntegrationStatusQuery, Result<UserIntegrationConnectionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserIntegrationConnectionRepository _repository;

    public GetGitHubUserIntegrationStatusQueryHandler(
        ICurrentUser currentUser,
        IUserIntegrationConnectionRepository repository)
    {
        _currentUser = currentUser;
        _repository = repository;
    }

    public async Task<Result<UserIntegrationConnectionDto>> Handle(
        GetGitHubUserIntegrationStatusQuery request,
        CancellationToken cancellationToken)
    {
        var connection = await _repository.GetActiveAsync(
            _currentUser.TenantId,
            _currentUser.UserId,
            GitHubUserOAuthRules.IntegrationKey,
            cancellationToken);
        if (connection is null)
        {
            return Result<UserIntegrationConnectionDto>.Success(
                UserIntegrationConnectionMapper.Disconnected(
                    GitHubUserOAuthRules.IntegrationKey));
        }

        return Result<UserIntegrationConnectionDto>.Success(
            UserIntegrationConnectionMapper.ToSafeDto(connection));
    }
}
