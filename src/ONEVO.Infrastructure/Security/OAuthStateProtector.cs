using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

public sealed class OAuthStateProtector : IOAuthStateProtector
{
    private readonly IDataProtector _protector;

    public OAuthStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ONEVO.GitHubUserOAuth.State.v1");
    }

    public string Protect(GitHubOAuthState state)
    {
        return _protector.Protect(JsonSerializer.Serialize(state));
    }

    public bool TryUnprotect(string protectedState, out GitHubOAuthState? state)
    {
        state = null;
        try
        {
            var json = _protector.Unprotect(protectedState);
            state = JsonSerializer.Deserialize<GitHubOAuthState>(json);
            return state is not null;
        }
        catch (Exception exception) when (
            exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return false;
        }
    }
}
