using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListRoleTemplates;

public sealed class ListRoleTemplatesQueryHandler
    : IRequestHandler<ListRoleTemplatesQuery, Result<IReadOnlyList<RoleTemplateDto>>>
{
    private readonly IRoleTemplateRepository _templates;

    public ListRoleTemplatesQueryHandler(IRoleTemplateRepository templates) => _templates = templates;

    public async Task<Result<IReadOnlyList<RoleTemplateDto>>> Handle(
        ListRoleTemplatesQuery request,
        CancellationToken ct)
    {
        var list = await _templates.ListAsync(ct);
        var dtos = list.Select(RoleTemplateMapper.ToDto).ToList();
        return Result<IReadOnlyList<RoleTemplateDto>>.Success(dtos);
    }
}
