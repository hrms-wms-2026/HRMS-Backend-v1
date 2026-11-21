using System.Text;

namespace ONEVO.Tests.Architecture.Schema;

/// <summary>
/// Task 39 ratchet. Every GUID id column without a foreign key, and every status/type column without a
/// check constraint, must be on an allow-list with a group and a reason. A new column fails until it gets
/// a key or constraint, or is listed. A listed column that got one fails until its line is removed, so the
/// lists only shrink.
///
/// To rewrite both lists from the model (keeping existing reasons, new lines get "todo"), run this class
/// with ONEVO_SCHEMA_INVENTORY_WRITE=1.
/// </summary>
public sealed class SchemaIntegrityInventoryTests
{
    private const string ForeignKeyList = "missing-foreign-keys.txt";
    private const string CheckList = "missing-check-constraints.txt";

    [Fact]
    public void EveryIdColumnWithoutAForeignKeyIsOnTheAllowList()
    {
        var actual = SchemaIntegrityInventory.IdColumnsWithoutForeignKey(SchemaIntegrityInventory.Model());
        AssertMatchesAllowList(ForeignKeyList, actual, "add the foreign key (see ReferenceForeignKeys)");
    }

    [Fact]
    public void EveryStatusColumnWithoutACheckConstraintIsOnTheAllowList()
    {
        var actual = SchemaIntegrityInventory.StatusColumnsWithoutCheck(SchemaIntegrityInventory.Model());
        AssertMatchesAllowList(CheckList, actual, "add the check constraint (see StatusCheckConstraints)");
    }

    [Theory]
    [InlineData(ForeignKeyList)]
    [InlineData(CheckList)]
    public void EveryAllowListEntryHasAGroupAndAReason(string fileName)
    {
        var offenders = AllowList.Read(fileName)
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value.Group) || string.IsNullOrWhiteSpace(entry.Value.Reason))
            .Select(entry => entry.Key)
            .ToList();

        Assert.True(offenders.Count == 0, $"{fileName}: entries need 'table.column | group | reason':{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }

    private static void AssertMatchesAllowList(string fileName, SortedSet<string> actual, string fix)
    {
        var listed = AllowList.Read(fileName);
        if (Environment.GetEnvironmentVariable("ONEVO_SCHEMA_INVENTORY_WRITE") == "1")
        {
            AllowList.Write(fileName, actual, listed);
            listed = AllowList.Read(fileName);
        }

        var unlisted = actual.Where(column => !listed.ContainsKey(column)).ToList();
        var stale = listed.Keys.Where(column => !actual.Contains(column)).ToList();
        var message = new StringBuilder();
        if (unlisted.Count > 0)
        {
            message.AppendLine($"New columns: {fix}, or list them in Schema/{fileName} with a reason:");
            unlisted.ForEach(column => message.AppendLine("  " + column));
        }

        if (stale.Count > 0)
        {
            message.AppendLine($"Fixed or removed columns: delete their lines from Schema/{fileName}:");
            stale.ForEach(column => message.AppendLine("  " + column));
        }

        Assert.True(message.Length == 0, message.ToString());
    }
}

internal static class AllowList
{
    public sealed record Entry(string Group, string Reason);

    public static SortedDictionary<string, Entry> Read(string fileName)
    {
        var result = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(PathOf(fileName)))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            result[parts[0]] = new Entry(parts.ElementAtOrDefault(1) ?? "", parts.ElementAtOrDefault(2) ?? "");
        }

        return result;
    }

    public static void Write(string fileName, IEnumerable<string> columns, IReadOnlyDictionary<string, Entry> existing)
    {
        var path = PathOf(fileName);
        var header = File.ReadAllLines(path).TakeWhile(line => line.StartsWith('#') || line.Length == 0);
        var lines = columns.Select(column => existing.TryGetValue(column, out var entry)
            ? $"{column} | {entry.Group} | {entry.Reason}"
            : $"{column} | todo | not reviewed yet");
        File.WriteAllLines(path, header.Concat(lines));
    }

    private static string PathOf(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "ONEVO.Tests.Architecture", "Schema", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Schema/{fileName} not found above {AppContext.BaseDirectory}.");
    }
}
