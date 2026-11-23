namespace ONEVO.Application.Common.Observability;

/// <summary>
/// Task 17: values that identify a person are masked before they reach a log line, because logs are shipped to a
/// central store that more people can read than the database. Keep enough to tell deliveries apart.
/// </summary>
public static class LogRedaction
{
    /// <summary><c>jane.doe@acme.com</c> → <c>j***@acme.com</c>. Anything that is not an address becomes <c>***</c>.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "***";
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1)
            return "***";
        return $"{email[0]}***{email[at..]}";
    }
}
