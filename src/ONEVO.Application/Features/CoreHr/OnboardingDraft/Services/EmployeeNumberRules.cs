using System.Text.RegularExpressions;

namespace ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;

/// <summary>
/// Shared employee-number format rules for onboarding suggestion, availability, draft save,
/// and finalization. Suggested defaults use <c>{COMPANY_CODE}-{0001}</c>; HR may edit to any
/// value matching <see cref="AllowedPattern"/> within the database max length.
/// </summary>
public static partial class EmployeeNumberRules
{
    public const int MaxLength = 20;
    public const int SequenceDigits = 4;

    /// <summary>Letters, digits, hyphen, underscore; no spaces. Case is preserved (not normalized).</summary>
    public static readonly Regex AllowedPattern = AllowedPatternRegex();

    public static string? NormalizeInput(string? employeeNumber)
        => employeeNumber?.Trim();

    public static bool IsValidFormat(string employeeNumber)
        => !string.IsNullOrEmpty(employeeNumber)
           && employeeNumber.Length <= MaxLength
           && AllowedPattern.IsMatch(employeeNumber);

    public static string FormatSuggested(string prefix, int sequence)
        => $"{prefix}-{sequence.ToString().PadLeft(SequenceDigits, '0')}";

    /// <summary>
    /// Company code is trimmed only (same as legal-entity settings). Empty after trim is invalid
    /// for suggestion. Prefix must leave room for <c>-0001</c> within <see cref="MaxLength"/>.
    /// </summary>
    public static bool TryNormalizePrefix(string? companyCode, out string prefix, out string? error)
    {
        prefix = (companyCode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(prefix))
        {
            error = "This company has no company code configured. Enter an employee number manually or set the company code first.";
            return false;
        }

        if (!AllowedPattern.IsMatch(prefix))
        {
            error = "Employee number can only contain letters, numbers, hyphens, and underscores.";
            return false;
        }

        var formattedSampleLength = prefix.Length + 1 + SequenceDigits;
        if (formattedSampleLength > MaxLength)
        {
            error = "This company code is too long to generate an employee number. Enter one manually.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Parses the trailing integer sequence from values like <c>DAPI-0005</c> or <c>DAPI-5</c>
    /// when the prefix (including hyphen) matches. Returns null when the suffix is not numeric.
    /// </summary>
    public static int? TryParseSequence(string employeeNumber, string prefix)
    {
        var expectedPrefix = prefix + "-";
        if (!employeeNumber.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return null;

        var suffix = employeeNumber[expectedPrefix.Length..];
        if (suffix.Length == 0 || !int.TryParse(suffix, out var sequence) || sequence < 1)
            return null;

        return sequence;
    }

    public static string InvalidFormatMessage
        => "Employee number can only contain letters, numbers, hyphens, and underscores.";

    public static string AlreadyInUseMessage
        => "This employee number is already in use.";

    public static string RequiredForFinalizeMessage
        => "An employee number is required to finalize onboarding.";

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPatternRegex();
}
