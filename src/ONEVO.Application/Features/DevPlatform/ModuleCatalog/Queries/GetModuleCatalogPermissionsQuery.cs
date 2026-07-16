using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.Queries;

public record GetModuleCatalogPermissionsQuery(string ModuleKey) : IRequest<Result<IReadOnlyList<ModulePermissionOwnershipDto>>>;
