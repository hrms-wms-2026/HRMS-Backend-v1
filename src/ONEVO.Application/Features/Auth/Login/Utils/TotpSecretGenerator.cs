namespace ONEVO.Application.Features.Auth.Login.Utils;

/// <summary>
/// Pure TOTP secret + QR-code-URI generation. No storage/entity coupling, so this is safely
/// shared between tenant and platform-admin MFA enrollment without either side depending on the
/// other's domain model. Extracted from EnableMfaCommandHandler/EnableAdminMfaCommandHandler,
/// which previously carried near-identical private copies of this logic.
/// </summary>
public static class TotpSecretGenerator
{
    public static (string Secret, string QrCodeUri) Generate(string email, string issuer)
    {
        var secretBytes = new byte[20];
        System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
        var secret = Base32Encode(secretBytes);

        var qrCodeUri =
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}"
            + $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";

        return (secret, qrCodeUri);
    }

    private static string Base32Encode(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(alphabet[(buffer >> bitsLeft) & 31]);
            }
        }
        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return result.ToString();
    }
}
