using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Queries.ListRoleTemplates;

public sealed record ListRoleTemplatesQuery : IRequest<Result<IReadOnlyList<RoleTemplateDto>>>;
