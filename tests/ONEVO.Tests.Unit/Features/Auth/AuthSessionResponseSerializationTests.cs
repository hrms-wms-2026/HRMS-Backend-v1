using System.Text.Json;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>
/// Guards the fixed architecture decision that tenant/admin web auth responses never carry
/// access/refresh/JWT/bearer tokens or the raw session id — only safe session metadata.
/// </summary>
public class AuthSessionResponseSerializationTests
{
    private static readonly string[] ForbiddenSubstrings =
        ["token", "jwt", "bearer", "session_id", "sessionId"];

    [Fact]
    public void LoginResponseDto_ToSessionResponse_JsonNeverContainsTokenShapedFields()
    {
        var dto = new LoginResponseDto(
            CsrfTokenHash: "11111111-1111-1111-1111-111111111111",
            CsrfToken: "raw-csrf-token-should-not-appear-in-json",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
            User: new CurrentUserDto(Guid.NewGuid(), Guid.NewGuid(), "a@b.com"),
            Permissions: ["employee.read"],
            ActiveModules: ["core_hr"]);

        var json = JsonSerializer.Serialize(dto.ToSessionResponse());

        foreach (var forbidden in ForbiddenSubstrings)
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);

        // The raw session id / csrf token values themselves must not leak either.
        Assert.DoesNotContain(dto.CsrfTokenHash, json);
        Assert.DoesNotContain(dto.CsrfToken, json);
    }

    [Fact]
    public void LoginResponseDto_MfaRequired_JsonNeverContainsTokenShapedFields()
    {
        var dto = new LoginResponseDto(
            CsrfTokenHash: string.Empty,
            CsrfToken: string.Empty,
            ExpiresAt: null,
            RequiresMfa: true,
            MfaChallenge: "opaque-mfa-challenge-credential");

        var json = JsonSerializer.Serialize(dto.ToSessionResponse());

        foreach (var forbidden in ForbiddenSubstrings)
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(dto.MfaChallenge!, json);
    }
}
