using System.Text.RegularExpressions;

namespace ONEVO.Tests.Architecture;

/// <summary>PostgreSQL-only SQL compiles fine and only fails at runtime, so source is scanned for it.</summary>
public sealed class NoPostgresSqlArchitectureTests
{
    private static readonly Regex PostgresSql = new(
        @"ON CONFLICT|\bRETURNING\b|SKIP LOCKED|FOR UPDATE|\bpg_[a-z]|::(uuid|text|jsonb|timestamptz|int|bigint|date)\b" +
        @"|set_config|current_setting|\bILIKE\b|\bnow\(\)|=\s*ANY\s*\(|\bunnest\(|Npgsql|gen_random_uuid|SET LOCAL" +
        @"|OPTION\s*\(\s*MAXDOP|WITH RECURSIVE|\bGREATEST\(|SELECT EXISTS|make_interval|-infinity",
        RegexOptions.Compiled);

    [Fact]
    public void NonMigrationSource_HasNoPostgresSql()
    {
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var code = lines[index].Trim();
                if (!code.StartsWith("//", StringComparison.Ordinal) && PostgresSql.IsMatch(code))
                {
                    offenders.Add($"{Path.GetRelativePath(SourceRoot(), path)}:{index + 1}: {code}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src");
    }
}
