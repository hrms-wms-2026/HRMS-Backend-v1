namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Requests;

public record InvitePlatformManagerRequest(string Email, string FullName, IReadOnlyList<Guid> RoleIds);
