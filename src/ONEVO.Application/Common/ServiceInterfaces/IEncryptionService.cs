namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    byte[] EncryptBytes(string plainText);
    string DecryptBytes(byte[] cipherBytes);
}
