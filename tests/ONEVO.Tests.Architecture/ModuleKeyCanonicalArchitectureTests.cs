using System.Text.RegularExpressions;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Module keys are canonical everywhere in source (DEC-040, DEC-041, DEC-042, DEC-048). Only the
/// temporary ModuleKeyAliases map and migrations may name a legacy module key.
/// </summary>
public sealed class ModuleKeyCanonicalArchitectureTests
{
    // Keys that are never anything but a legacy module identity.
    private static readonly Regex UnambiguousLegacyKey = new(
        "\"(org_structure|activity_monitoring|identity_verification|productivity_analytics|worksync_foundation" +
        "|objectives_milestones|planning_sprints|desktop_agent_gateway|discrepancy_engine|exception_engine)\"",
        RegexOptions.Compiled);

    // Legacy keys that are also ordinary domain words; flagged only where a module key is expected.
    private const string AmbiguousKeys = "(leave|projects|tasks|boards|attendance|employees)";

    private static readonly Regex AmbiguousLegacyKeyInModuleContext = new(
        $"RequireAnyModule\\([^)]*\"{AmbiguousKeys}\"" +
        $"|(ModuleKeysJson|IncludedModulesJson|SelectedModulesJson)\\s*=\\s*\"\"\"\\[[^\\]]*\"{AmbiguousKeys}\"" +
        $"|Perm\\(\"[^\"]+\",\\s*\"[^\"]*\",\\s*\"{AmbiguousKeys}\"\\)" +
        $"|\\bModule\\s*=\\s*\"{AmbiguousKeys}\"",
        RegexOptions.Compiled);

    // Dictionary keys are module keys only in ModuleAutoGrants (elsewhere they are table names or labels).
    private static readonly Regex AutoGrantLegacyKey = new(
        $"^\\s*\\[\"{AmbiguousKeys}\"\\]\\s*=", RegexOptions.Compiled);

    [Fact]
    public void Source_UsesNoLegacyModuleKeysOutsideAliasMap()
    {
        var root = SourceRoot();
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path)
                || path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(path) == "ModuleKeyAliases.cs")
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var code = lines[index].Trim();
                if (code.StartsWith("//", StringComparison.Ordinal))
                    continue;
                var isAutoGrants = Path.GetFileName(path) == "ModuleAutoGrants.cs";
                if (UnambiguousLegacyKey.IsMatch(code)
                    || AmbiguousLegacyKeyInModuleContext.IsMatch(lines[index])
                    || (isAutoGrants && AutoGrantLegacyKey.IsMatch(lines[index])))
                    offenders.Add($"{Path.GetRelativePath(root, path)}:{index + 1}: {code}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("src not found"), "src");
    }
}
