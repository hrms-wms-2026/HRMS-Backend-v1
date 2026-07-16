using System.Text;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

/// <summary>
/// No-op encryption for development. Replace with AES-256 implementation in production.
/// </summary>
public class NoOpEncryptionService : IEncryptionService
{
    public string Encrypt(string plainText) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    public string Decrypt(string cipherText) => Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
    public byte[] EncryptBytes(string plainText) => Encoding.UTF8.GetBytes(plainText);
    public string DecryptBytes(byte[] cipherBytes) => Encoding.UTF8.GetString(cipherBytes);
}
