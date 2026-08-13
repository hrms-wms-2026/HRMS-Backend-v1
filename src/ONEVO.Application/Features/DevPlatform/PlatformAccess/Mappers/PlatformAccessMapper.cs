using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;

public static class PlatformAccessMapper
{
    public static PlatformUserResponse Map(PlatformUser user, string role)
    {
        return new PlatformUserResponse(
            user.Id,
            user.Email,
            user.FullName,
            role,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt);
    }

    public static PlatformUserDetailResponse MapDetail(PlatformUser user, IEnumerable<PlatformRole> roles)
    {
        var mappedRoles = roles.Select(Map).ToList();

        return new PlatformUserDetailResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt,
            mappedRoles);
    }

    public static PlatformRoleResponse Map(PlatformRole role)
    {
        return new PlatformRoleResponse(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            role.CreatedAt);
    }

    public static PlatformRoleDetailResponse MapDetail(PlatformRole role, IEnumerable<string> permissions)
    {
        return new PlatformRoleDetailResponse(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem,
            role.CreatedAt,
            permissions.ToList());
    }

    public static PlatformPermissionResponse Map(PlatformPermissionDefinition definition)
    {
        return new PlatformPermissionResponse(
            definition.Code,
            definition.ModuleKey,
            definition.Description,
            definition.IsHighRisk);
    }

    public static PlatformUserSessionResponse Map(PlatformUserSession session)
    {
        return new PlatformUserSessionResponse(
            session.Id,
            session.AccountId,
            session.UserAgent,
            session.IpAddress,
            session.ExpiresAt,
            session.CreatedAt,
            session.RevokedAt,
            session.RevokedAt != null);
    }

    public static PlatformAuthEventResponse Map(PlatformAuthEvent authEvent)
    {
        return new PlatformAuthEventResponse(
            authEvent.Id,
            authEvent.UserId,
            authEvent.EventType,
            authEvent.SourceIp,
            authEvent.UserAgent,
            authEvent.CreatedAt);
    }
}
