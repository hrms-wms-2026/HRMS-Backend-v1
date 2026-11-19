namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The spec 5.5 split mapping, by expansion slot: the old series' i-th slot from the split point becomes the new
/// series' i-th slot. A materialized occurrence on old slot i is remapped (keeping its task id) to new slot i; one with
/// no new slot - or not on the old expansion at all - is surplus; a suppression travels with its slot. Order is kept,
/// so the new keys are strictly increasing.
/// </summary>
public sealed record MeetingSplitPlan(
    IReadOnlyList<(DateTimeOffset OldKey, DateTimeOffset NewKey)> Remaps,
    IReadOnlyList<DateTimeOffset> Surplus,
    IReadOnlyList<(DateTimeOffset OldKey, DateTimeOffset NewKey)> SuppressionCopies)
{
    public static MeetingSplitPlan Create(IReadOnlyList<DateTimeOffset> oldExpansion, IEnumerable<DateTimeOffset> materialized,
        IEnumerable<DateTimeOffset> suppressed, IReadOnlyList<DateTimeOffset> newExpansion)
    {
        var slotOf = oldExpansion.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index);
        DateTimeOffset? NewKeyFor(DateTimeOffset oldKey)
            => slotOf.TryGetValue(oldKey, out var slot) && slot < newExpansion.Count ? newExpansion[slot] : null;

        var remaps = new List<(DateTimeOffset, DateTimeOffset)>();
        var surplus = new List<DateTimeOffset>();
        foreach (var key in materialized.Order())
        {
            if (NewKeyFor(key) is { } newKey)
                remaps.Add((key, newKey));
            else
                surplus.Add(key);
        }
        var copies = suppressed.Order().Select(key => (key, NewKeyFor(key))).Where(x => x.Item2 is not null)
            .Select(x => (x.key, x.Item2!.Value)).ToList();
        return new MeetingSplitPlan(remaps, surplus, copies);
    }
}

/// <summary>RRULE edits a split needs.</summary>
public static class MeetingRules
{
    /// <summary>The rule ended just before <paramref name="until"/>: any UNTIL/COUNT is replaced by UNTIL.</summary>
    public static string WithUntil(string rule, DateTimeOffset until)
        => string.Join(';', rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase))
            .Append($"UNTIL={until.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}"));

    /// <summary>The same rule for what is left after <paramref name="consumed"/> occurrences: COUNT is reduced; a rule
    /// without COUNT is unchanged. Null when nothing is left.</summary>
    public static string? Remaining(string? rule, int consumed)
    {
        if (rule is null)
            return null;
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var countIndex = parts.FindIndex(p => p.StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase));
        if (countIndex < 0 || !int.TryParse(parts[countIndex]["COUNT=".Length..], out var count))
            return rule;
        var left = count - consumed;
        if (left <= 0)
            return null;
        parts[countIndex] = $"COUNT={left}";
        return string.Join(';', parts);
    }
}
