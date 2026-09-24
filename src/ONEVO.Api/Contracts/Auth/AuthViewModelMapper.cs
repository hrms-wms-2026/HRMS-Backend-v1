using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Api.Contracts.Auth;

public static class AuthViewModelMapper
{
    public static AuthSessionViewModel ToViewModel(this AuthSessionResponseDto dto) => new(
        dto.Authenticated,
        dto.User is null ? null : new CurrentUserViewModel(dto.User.Email),
        dto.Permissions,
        dto.ActiveModules,
        dto.MustChangePassword,
        dto.MfaRequired,
        dto.LegalAcceptanceRequired,
        dto.PendingLegalDocuments?.Select(ToViewModel).ToList(),
        dto.ExpiresAt,
        dto.ContinueUrl,
        dto.Workspace is null ? null : new WorkspaceViewModel(dto.Workspace.Slug, dto.Workspace.DisplayName)
    );

    public static PendingLegalDocumentViewModel ToViewModel(this PendingLegalDocumentDto dto) => new(
        dto.DocumentType,
        dto.Version,
        dto.Title,
        dto.EffectiveAt,
        dto.ContentUrl,
        dto.ContentEndpoint,
        dto.ContentHash
    );

    public static TenantSessionExchangeViewModel ToViewModel(this TenantSessionExchangeResponseDto dto) => new(
        dto.Authenticated,
        dto.RedirectRequired,
        new TenantSessionExchangeUserViewModel(dto.User.Email),
        new WorkspaceViewModel(dto.Workspace.Slug, dto.Workspace.DisplayName),
        dto.ContinueUrl,
        dto.ExpiresAt
    );
}
