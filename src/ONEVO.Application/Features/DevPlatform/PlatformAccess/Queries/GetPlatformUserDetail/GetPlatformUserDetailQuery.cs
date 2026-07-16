using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformUserDetail;

public record GetPlatformUserDetailQuery(Guid UserId) : IRequest<PlatformUserDetailResponse>;
