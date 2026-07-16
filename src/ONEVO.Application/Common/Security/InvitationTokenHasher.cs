using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Common.Security;

/// <summary>
/// Hashes the raw invite token the same way <see cref="Features.Tenancy.Commands.InviteTenantAdmin.InviteTenantAdminCommandHandler"/> does when issuing invites.
/// </summary>
public static class InvitationTokenHasher
{
    public static string Hash(string plaintextToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
