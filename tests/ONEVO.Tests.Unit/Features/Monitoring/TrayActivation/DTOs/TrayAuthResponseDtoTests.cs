using System.Text.Json;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation.DTOs;

public class TrayAuthResponseDtoTests
{
    [Fact]
    public void TrayAuthResponseDto_SerializesLegalAcceptanceFieldsWithSnakeCaseNames()
    {
        var dto = new TrayAuthResponseDto(
            AccessToken: "token", ExpiresInSeconds: 3600, RefreshToken: "refresh", RefreshExpiresInSeconds: 7776000,
            RequiresLegalAcceptance: true,
            PendingLegalDocuments: new[]
            {
                new PendingLegalDocumentDto("privacy_policy", "2.0", "Privacy Policy", DateTimeOffset.UtcNow, null, "/api/v1/legal/documents/privacy_policy/2.0", "hash")
            });

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"legal_acceptance_required\":true", json);
        Assert.Contains("\"pending_legal_documents\":[", json);
    }

    [Fact]
    public void TrayAuthResponseDto_SerializesLegalChallengeAndCsrfTokenWithSnakeCaseNames()
    {
        var dto = new TrayAuthResponseDto(
            AccessToken: "token", ExpiresInSeconds: 3600, RefreshToken: "refresh", RefreshExpiresInSeconds: 7776000,
            RequiresLegalAcceptance: true,
            LegalChallenge: "raw-challenge",
            LegalCsrfToken: "raw-csrf");

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"legal_challenge\":\"raw-challenge\"", json);
        Assert.Contains("\"legal_csrf_token\":\"raw-csrf\"", json);
    }
}
