using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Mappers;

internal static class LoginMapper
{
    public static CurrentUserDto ToCurrentUserDto(User user) =>
        new(user.Id, user.TenantId, user.Email);

    public static WorkspaceResponseDto ToWorkspaceResponseDto(Tenant tenant) =>
        new(tenant.Slug, tenant.Name);

    public static LoginResponseDto ToPasswordChangeRequired(User user, Tenant tenant) =>
        new(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            User: ToCurrentUserDto(user),
            Workspace: ToWorkspaceResponseDto(tenant),
            RequiresPasswordChange: true);

    public static LoginResponseDto ToMfaRequired(User user, Tenant tenant, string mfaChallenge) =>
        new(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            User: ToCurrentUserDto(user),
            Workspace: ToWorkspaceResponseDto(tenant),
            RequiresMfa: true,
            MfaChallenge: mfaChallenge);

    public static LoginResponseDto ToLegalAcceptanceRequired(
        User user,
        Tenant tenant,
        string legalChallenge,
        string legalCsrfToken,
        IReadOnlyList<PendingLegalDocumentDto> pendingDocuments) =>
        new(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            User: ToCurrentUserDto(user),
            Workspace: ToWorkspaceResponseDto(tenant),
            RequiresLegalAcceptance: true,
            LegalChallenge: legalChallenge,
            LegalCsrfToken: legalCsrfToken,
            PendingLegalDocuments: pendingDocuments);
}
