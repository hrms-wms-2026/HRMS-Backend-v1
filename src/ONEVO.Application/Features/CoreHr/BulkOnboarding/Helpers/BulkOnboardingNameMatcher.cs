namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Helpers;

public sealed record NameMatchSuggestion(string Label, string Confidence);

/// <summary>
/// Deterministic fuzzy matcher for bulk-onboarding imported setup names.
/// Confidence: exact | high | medium. Low-confidence guesses are omitted.
/// </summary>
public static class BulkOnboardingNameMatcher
{
    private static readonly string[] SafeSuffixes = ["department", "dept", "team", "group"];

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var collapsed = string.Join(' ', value.Trim().ToLowerInvariant().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        foreach (var suffix in SafeSuffixes)
        {
            if (collapsed.EndsWith(" " + suffix, StringComparison.Ordinal))
                collapsed = collapsed[..^(suffix.Length + 1)].TrimEnd();
            else if (collapsed == suffix)
                return string.Empty;
        }

        return collapsed;
    }

    public static NameMatchSuggestion? FindBest(string importedValue, IEnumerable<string> candidates)
    {
        var imported = Normalize(importedValue);
        if (imported.Length == 0)
            return null;

        NameMatchSuggestion? best = null;
        var bestScore = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = Normalize(candidate);
            if (normalizedCandidate.Length == 0)
                continue;

            if (imported == normalizedCandidate)
                return new NameMatchSuggestion(candidate, "exact");

            var confidence = Classify(imported, normalizedCandidate, out var score);
            if (confidence is null)
                continue;

            if (score < bestScore ||
                (score == bestScore && ConfidenceRank(confidence) > ConfidenceRank(best?.Confidence)))
            {
                bestScore = score;
                best = new NameMatchSuggestion(candidate, confidence);
            }
        }

        return best;
    }

    private static string? Classify(string imported, string candidate, out int score)
    {
        if (candidate.StartsWith(imported, StringComparison.Ordinal) ||
            imported.StartsWith(candidate, StringComparison.Ordinal) ||
            candidate.Contains(imported, StringComparison.Ordinal) ||
            imported.Contains(candidate, StringComparison.Ordinal))
        {
            // Prefer shorter distance when one contains the other.
            score = Math.Abs(candidate.Length - imported.Length);
            if (imported.Length >= 3 && (candidate.StartsWith(imported, StringComparison.Ordinal) || imported.StartsWith(candidate, StringComparison.Ordinal)))
                return score <= 2 ? "high" : "medium";
            if (imported.Length >= 4)
                return score <= 4 ? "medium" : null;
            return null;
        }

        var maxLen = Math.Max(imported.Length, candidate.Length);
        var distance = Levenshtein(imported, candidate);
        score = distance;

        // Bound edit distance relative to length so random short typos do not match.
        if (distance == 0)
            return "exact";
        if (distance == 1 && maxLen >= 4)
            return "high";
        if (distance == 2 && maxLen >= 6)
            return "high";
        if (distance <= 3 && maxLen >= 10)
            return "medium";
        if (distance <= 2 && maxLen >= 5)
            return "medium";

        return null;
    }

    private static int ConfidenceRank(string? confidence) => confidence switch
    {
        "exact" => 3,
        "high" => 2,
        "medium" => 1,
        _ => 0
    };

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
