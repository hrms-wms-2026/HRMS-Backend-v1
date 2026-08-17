namespace ONEVO.Application.Features.CoreHr.Employee.Helpers;

public static class BankAccountMasker
{
    /// <summary>Formats a decrypted account number as "****1234" - never call with the encrypted
    /// ciphertext, only with the plaintext returned by IEncryptionService.Decrypt.</summary>
    public static string Mask(string plainAccountNumber)
    {
        var digitsOnly = new string(plainAccountNumber.Where(char.IsDigit).ToArray());
        return digitsOnly.Length <= 4
            ? new string('*', digitsOnly.Length)
            : $"****{digitsOnly[^4..]}";
    }
}
