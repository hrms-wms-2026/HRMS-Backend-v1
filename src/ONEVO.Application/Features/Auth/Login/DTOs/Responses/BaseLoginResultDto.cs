namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

public sealed record BaseLoginWorkspaceOptionDto(string Slug, string DisplayName);

public sealed record BaseLoginWorkspaceSelectionRequiredDto(
    string LoginChallenge,
    IReadOnlyList<BaseLoginWorkspaceOptionDto> Workspaces,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Base-domain login outcome. Exactly one of Session/WorkspaceSelection is non-null on success;
/// zero matches, wrong password, or overflow instead return a generic Result failure.
/// </summary>
public sealed record BaseLoginResultDto(
    LoginResponseDto? Session,
    BaseLoginWorkspaceSelectionRequiredDto? WorkspaceSelection);
