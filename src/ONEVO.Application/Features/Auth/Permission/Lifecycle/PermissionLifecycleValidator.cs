namespace ONEVO.Application.Features.Auth.Permission.Lifecycle;

/// <summary>
/// Checks the lifecycle declarations against the catalogue. A unit test runs it on the real lists,
/// so a bad declaration fails CI instead of a deploy; the synchronizer runs it again before it
/// changes anything.
/// </summary>
public static class PermissionLifecycleValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyCollection<PermissionDefinition> catalogue, PermissionLifecycleRules rules)
    {
        var errors = new List<string>();
        var live = catalogue.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var duplicate in catalogue.GroupBy(p => p.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"Catalogue lists '{duplicate.Key}' more than once.");

        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rename in rules.Renames)
        {
            if (rename.From == rename.To)
                errors.Add($"Rename '{rename.From}' points at itself.");
            else if (!renames.TryAdd(rename.From, rename.To))
                errors.Add($"'{rename.From}' is renamed more than once.");

            if (live.Contains(rename.From))
                errors.Add($"Rename source '{rename.From}' is still in the catalogue; take it out of the catalogue.");
        }

        foreach (var from in renames.Keys)
        {
            var target = ResolveRename(from, renames, out var cycle);
            if (cycle)
                errors.Add($"Renames starting at '{from}' form a cycle.");
            else if (!live.Contains(target))
                errors.Add($"Rename '{from}' -> '{target}' ends at a code that is not in the catalogue.");
        }

        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var retirement in rules.Retirements)
        {
            if (!retired.Add(retirement.Code))
                errors.Add($"'{retirement.Code}' is retired more than once.");
            if (live.Contains(retirement.Code))
                errors.Add($"Retired code '{retirement.Code}' is still in the catalogue.");
            if (renames.ContainsKey(retirement.Code))
                errors.Add($"'{retirement.Code}' is both renamed and retired; the rename already keeps its grants.");
        }

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in rules.DefaultGrants)
        {
            if (!granted.Add(grant.Code))
                errors.Add($"'{grant.Code}' has more than one default-grant entry.");
            if (!live.Contains(grant.Code))
                errors.Add($"Default grant for '{grant.Code}', which is not in the catalogue.");
            if (grant.Targets.Count == 0)
                errors.Add($"Default grant for '{grant.Code}' names no target.");

            foreach (var target in grant.Targets)
            {
                if (target.Kind == DefaultGrantTargetKind.RoleTemplate && target.TemplateId is null)
                    errors.Add($"Default grant for '{grant.Code}' has a template target without a template id.");
                if (target.Kind == DefaultGrantTargetKind.HoldersOf && string.IsNullOrWhiteSpace(target.Code))
                    errors.Add($"Default grant for '{grant.Code}' has a holders-of target without a code.");
                if (target.Kind == DefaultGrantTargetKind.HoldersOf && target.Code == grant.Code)
                    errors.Add($"Default grant for '{grant.Code}' copies from its own holders.");
            }
        }

        return errors;
    }

    /// <summary>Follows a rename chain (a -> b -> c) to its last code.</summary>
    public static string ResolveRename(string from, IReadOnlyDictionary<string, string> renames, out bool cycle)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var current = from;
        while (renames.TryGetValue(current, out var next))
        {
            if (!seen.Add(next))
            {
                cycle = true;
                return current;
            }

            current = next;
        }

        cycle = false;
        return current;
    }
}
