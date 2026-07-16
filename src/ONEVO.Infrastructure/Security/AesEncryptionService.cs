using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Configuration;

namespace ONEVO.Infrastructure.Security;

public class AesEncryptionService : IEncryptionService
{
    // Layout: [nonce(12)][tag(16)][ciphertext]
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesEncryptionService(IOptions<EncryptionOptions> options)
    {
        var masterKey = options.Value.MasterKey;
        if (string.IsNullOrWhiteSpace(masterKey))
            throw new InvalidOperationException("Encryption:MasterKey is not configured.");

        // Derive a stable 256-bit key from the master key string
        _key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(masterKey),
            salt: Encoding.UTF8.GetBytes("ONEVO-AES256-v1"),
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    public string Encrypt(string plainText)
    {
        var cipher = EncryptCore(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToBase64String(cipher);
    }

    public string Decrypt(string cipherText)
    {
        var plain = DecryptCore(Convert.FromBase64String(cipherText));
        return Encoding.UTF8.GetString(plain);
    }

    public byte[] EncryptBytes(string plainText) =>
        EncryptCore(Encoding.UTF8.GetBytes(plainText));

    public string DecryptBytes(byte[] cipherBytes) =>
        Encoding.UTF8.GetString(DecryptCore(cipherBytes));

    private byte[] EncryptCore(byte[] plainBytes)
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    private byte[] DecryptCore(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext is too short.");

        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var ciphertext = data[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
