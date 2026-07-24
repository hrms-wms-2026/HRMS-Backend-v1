using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Services.AgentGateway;

public sealed partial class NetworkEvidenceHasher : INetworkEvidenceHasher
{
    private readonly byte[] _key;

    public NetworkEvidenceHasher(IOptions<EncryptionOptions> options)
    {
        var masterKey = options.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(masterKey))
        {
            throw new InvalidOperationException(
                "Encryption:MasterKey is required for network evidence protection.");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }

    public string? Protect(Guid tenantId, string? locallyHashedIdentifier)
    {
        if (string.IsNullOrWhiteSpace(locallyHashedIdentifier))
            return null;

        var normalized = locallyHashedIdentifier.Trim().ToLowerInvariant();
        if (normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Contains('-', StringComparison.Ordinal) ||
            normalized.Length % 2 != 0 ||
            !LocallyHashedIdentifierPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Network identifier must be a 32 to 128 character hexadecimal local hash.",
                nameof(locallyHashedIdentifier));
        }

        var message = Encoding.UTF8.GetBytes($"{tenantId:N}:{normalized}");
        var protectedHash = HMACSHA256.HashData(_key, message);
        return Convert.ToHexString(protectedHash).ToLowerInvariant();
    }

    [GeneratedRegex(@"\A[0-9a-f]{32,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocallyHashedIdentifierPattern();
}
