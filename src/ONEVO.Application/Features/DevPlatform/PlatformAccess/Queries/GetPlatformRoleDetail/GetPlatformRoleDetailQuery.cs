using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformRoleDetail;

public record GetPlatformRoleDetailQuery(Guid RoleId) : IRequest<PlatformRoleDetailResponse>;
