using System.Security.Cryptography;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Mfa;

/// <summary>
/// RFC 6238 TOTP implementation. Allows ±1 time step (30s window each side).
/// </summary>
public class OtpNetTotpService : ITotpService
{
    public bool Verify(string base32Secret, string code)
    {
        if (!int.TryParse(code, out var codeInt) || code.Length != 6)
            return false;

        try
        {
            var key = Base32Decode(base32Secret);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Check current step and ±1 window (90 seconds total)
            for (int delta = -1; delta <= 1; delta++)
            {
                var step = (now / 30) + delta;
                var expected = ComputeTotp(key, step);
                if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(expected.ToString("D6")),
                    System.Text.Encoding.ASCII.GetBytes(code)))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static int ComputeTotp(byte[] key, long step)
    {
        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(stepBytes);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(stepBytes);

        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24)
                  | ((hash[offset + 1] & 0xFF) << 16)
                  | ((hash[offset + 2] & 0xFF) << 8)
                  | (hash[offset + 3] & 0xFF);

        return value % 1_000_000;
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        base32 = base32.ToUpperInvariant().TrimEnd('=');

        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;

        foreach (var c in base32)
        {
            var idx = alphabet.IndexOf(c);
            if (idx < 0) throw new ArgumentException("Invalid base32 character.");
            buffer = (buffer << 5) | idx;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)(buffer >> bitsLeft));
            }
        }
        return [.. output];
    }
}
