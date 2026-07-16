using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformPermissions;

public record ListPlatformPermissionsQuery : IRequest<IReadOnlyList<PlatformPermissionResponse>>;
