using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformAuthEvents;

public record ListPlatformAuthEventsQuery : IRequest<IReadOnlyList<PlatformAuthEventResponse>>;
