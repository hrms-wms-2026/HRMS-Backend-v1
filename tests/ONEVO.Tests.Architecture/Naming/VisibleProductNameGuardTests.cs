using System.Text.RegularExpressions;
using FluentAssertions;

namespace ONEVO.Tests.Architecture.Naming;

/// <summary>
/// The product is Crownus. No old product name (Onevo / Onexso) may remain anywhere a user,
/// tester or server admin can see it: string literals, config, SQL, scripts, CI and the
/// Postman collection. Code names (namespaces, project paths, identifiers outside string
/// literals) are renamed separately and are allowed here, as is the AES salt, which must
/// never change (it would make stored encrypted data unreadable).
/// </summary>
public sealed class VisibleProductNameGuardTests
{
    private static readonly Regex OldName = new("onevo|onexso", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Removed from a line or literal before OldName is checked.
    private static readonly Regex Allowed = new(
        @"ONEVO-AES256-v1|\bONEVO\.[A-Z][A-Za-z0-9_]*|(?i:oneverz)",
        RegexOptions.Compiled);

    private static readonly Regex CsStringLiteral = new(
        "\"\"\"[\\s\\S]*?\"\"\"|@\"(?:[^\"]|\"\")*\"|\"(?:[^\"\\\\\\n]|\\\\.)*\"",
        RegexOptions.Compiled);

    // Dev-only demo seeders return early outside Development/Test; their demo names never reach a server.
    private static readonly Regex DevOnlySeeder = new(
        @"[\\/]Persistence[\\/]Seeders[\\/](Dapi[^\\/]*|WorkManagementDapiDemo[^\\/]*|WorkManagementSampleData[^\\/]*)\.cs$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules", ".git"];

    [Fact]
    public void NoOldProductNameInVisibleSourcesOrConfig()
    {
        var root = FindRepositoryRoot();
        var findings = new List<string>();

        foreach (var file in FilesInScope(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (DevOnlySeeder.IsMatch(file))
                    continue;

                foreach (Match literal in CsStringLiteral.Matches(text))
                {
                    if (OldName.IsMatch(Allowed.Replace(literal.Value, string.Empty)))
                        findings.Add($"{relative}:{LineOf(text, literal.Index)}: {Shorten(literal.Value)}");
                }
            }
            else
            {
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (OldName.IsMatch(Allowed.Replace(lines[i], string.Empty)))
                        findings.Add($"{relative}:{i + 1}: {Shorten(lines[i].Trim())}");
                }
            }
        }

        findings.Should().BeEmpty(
            "the product is Crownus and no old product name may be visible; found {0} match(es):\n{1}",
            findings.Count,
            string.Join('\n', findings.Take(300)));
    }

    private static IEnumerable<string> FilesInScope(string root)
    {
        foreach (var file in Walk(Path.Combine(root, "src")))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }

        foreach (var file in Walk(Path.Combine(root, "ops")))
            yield return file;

        foreach (var file in Walk(Path.Combine(root, ".github")))
            yield return file;

        foreach (var file in Walk(Path.Combine(root, "docs", "postman-request")))
            yield return file;

        var envExample = Path.Combine(root, ".env.example");
        if (File.Exists(envExample))
            yield return envExample;
    }

    private static IEnumerable<string> Walk(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                continue;
            foreach (var file in Walk(child))
                yield return file;
        }
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string Shorten(string value)
    {
        var single = value.Replace('\n', ' ').Replace('\r', ' ');
        return single.Length <= 140 ? single : single[..140] + "…";
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
