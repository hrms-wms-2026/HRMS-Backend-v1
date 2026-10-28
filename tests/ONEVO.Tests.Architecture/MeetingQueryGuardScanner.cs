using System.Text.RegularExpressions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Source scans behind the meeting query-filter guards (meetings-as-tasks Phase 2 spec 2.2, 5.1). Text scans cannot
/// see every shape (e.g. a bare IgnoreQueryFilters() on another root that navigates to a task); those are still caught
/// one level up, because every file that calls IgnoreQueryFilters( at all must be on
/// TenantIsolationArchitectureTests.IgnoreQueryFilters_UsageIsExplicitlyAllowlisted's list.
/// </summary>
public static class MeetingQueryGuardScanner
{
    private static readonly Regex MemberStart =
        new(@"^\s*(public|private|internal|protected)\b[^=(]*?\b(\w+)\s*(<[^>]*>)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex TouchesWorkTasks = new(@"\bWorkTasks\b|\bSet<WorkTask>\s*\(", RegexOptions.Compiled);

    private static readonly Regex ExcludeMeetingsToken = new(@"\bExcludeMeetings\b", RegexOptions.Compiled);

    // An EF raw-SQL call whose literal mentions tasks (the literal ends at the first ';').
    private static readonly Regex RawSqlCallOnTasks = new(
        @"(ExecuteSql\w*|SqlQuery\w*|FromSql\w*)\s*(<[^>]*>)?\s*\(\s*\$?@?""""?""?[^;]*?\btasks\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // SQL against the tasks table anywhere in the file - a const, a variable, an NpgsqlCommand, Dapper. Case-sensitive
    // keywords, so prose such as "move all tasks from this status" never matches.
    private static readonly Regex SqlKeywordOnTasks = new(@"\b(FROM|JOIN|UPDATE|INTO)\s+""?tasks""?\b", RegexOptions.Compiled);

    /// <summary>Members that call a bare IgnoreQueryFilters() and touch WorkTasks anywhere in the same member - the
    /// call and the query may sit in different statements.</summary>
    public static IReadOnlyList<string> MembersWithBareWorkTaskIgnore(string source)
        => Members(source)
            .Where(m => m.Body.Contains(".IgnoreQueryFilters()", StringComparison.Ordinal) && TouchesWorkTasks.IsMatch(m.Body))
            .Select(m => m.Name)
            .Distinct()
            .ToList();

    /// <summary>Any mention of the ExcludeMeetings filter name, however it is spelled out.</summary>
    public static bool MentionsExcludeMeetings(string source) => ExcludeMeetingsToken.IsMatch(source);

    public static bool HasRawSqlOnTasks(string source) => RawSqlCallOnTasks.IsMatch(source) || SqlKeywordOnTasks.IsMatch(source);

    /// <summary>Each member's text: from its declaration line to the next declaration line.</summary>
    private static IEnumerable<(string Name, string Body)> Members(string source)
    {
        var name = "";
        var body = new System.Text.StringBuilder();
        foreach (var line in source.Split('\n'))
        {
            var match = MemberStart.Match(line);
            if (match.Success)
            {
                if (body.Length > 0)
                    yield return (name, body.ToString());
                name = match.Groups[2].Value;
                body.Clear();
            }
            body.Append(line).Append('\n');
        }
        if (body.Length > 0)
            yield return (name, body.ToString());
    }
}
