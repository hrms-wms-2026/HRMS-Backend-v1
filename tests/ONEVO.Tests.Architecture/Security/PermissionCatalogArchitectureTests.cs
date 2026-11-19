using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Architecture.Security;

/// <summary>
/// Task 32 guards: PermissionCatalog is the only place a tenant permission code is written as text,
/// every constant has a definition, codes are unique, and the seeders, auto-grants and derived codes
/// only use codes the catalogue knows.
/// </summary>
public class PermissionCatalogArchitectureTests
{
    private const string CatalogueFileName = "PermissionCatalog.cs";

    // projects:access is retired but WorkApprovalEngine still names it (value kept in task 32; task 33
    // decides the replacement). Any other use of a retired constant fails.
    private static readonly HashSet<string> AllowedRetiredReferences = new(StringComparer.Ordinal)
    {
        "WorkApprovalEngine.cs|PermissionCatalog.Retired.ProjectsAccess",
    };

    private static readonly Regex StringLiteral = new("(?<![@$\\w])\"([^\"\\\\\\r\\n]*)\"", RegexOptions.Compiled);

    private static readonly Regex PermissionCallWithLiteral = new(
        "(RequirePermission|RequireAnyPermission|HasPermission|HasPermissionCodeAsync|UserHasPermissionCodeAsync)\\s*\\([^)]*\"",
        RegexOptions.Compiled);

    private static readonly Regex CatalogueReference = new(
        "PermissionCatalog\\.Retired\\.[A-Za-z]+", RegexOptions.Compiled);

    [Fact]
    public void NoPermissionCodeLiteral_OutsideTheCatalogue()
    {
        var codes = PermissionCatalog.All
            .Select(d => d.Code)
            .Where(c => c != PermissionCatalog.SuperAdmin)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();
        foreach (var (file, lineNumber, code) in SourceCodeLines())
        {
            foreach (Match m in StringLiteral.Matches(code))
            {
                if (codes.Contains(m.Groups[1].Value))
                    offenders.Add($"{file}:{lineNumber}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Use PermissionCatalog constants instead of permission code text:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void PermissionAttributesAndChecks_NeverTakeAStringLiteral()
    {
        // Catches a typo too: a literal that is not a known code still fails here.
        var offenders = SourceCodeLines()
            .Where(l => PermissionCallWithLiteral.IsMatch(l.Code))
            .Select(l => $"{l.File}:{l.Line}: {l.Code.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Pass a PermissionCatalog constant to RequirePermission, RequireAnyPermission and HasPermission:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void RetiredConstants_AreNotUsed()
    {
        var offenders = new List<string>();
        foreach (var (file, lineNumber, code) in SourceCodeLines())
        {
            foreach (Match m in CatalogueReference.Matches(code))
            {
                if (!AllowedRetiredReferences.Contains($"{Path.GetFileName(file)}|{m.Value}"))
                    offenders.Add($"{file}:{lineNumber}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Retired permission codes cannot be held by anyone:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EndpointAttributes_OnlyUseLiveCatalogueCodes()
    {
        var bad = new List<string>();
        foreach (var type in typeof(RequirePermissionAttribute).Assembly.GetTypes())
        {
            var members = new List<MemberInfo> { type };
            members.AddRange(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            foreach (var member in members)
            {
                var codes = member.GetCustomAttributes<RequirePermissionAttribute>(false).Select(a => a.Permission)
                    .Concat(member.GetCustomAttributes<RequireAnyPermissionAttribute>(false).SelectMany(a => a.Permissions));
                foreach (var code in codes)
                {
                    var definition = PermissionCatalog.Find(code);
                    if (definition is null || definition.Kind is PermissionKind.Retired or PermissionKind.NotSeeded)
                        bad.Add($"{type.Name}.{member.Name}: '{code}' ({definition?.Kind.ToString() ?? "unknown"})");
                }
            }
        }

        Assert.True(bad.Count == 0, "Endpoint permission codes must be live catalogue codes:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void EveryCatalogueConstant_HasExactlyOneDefinition_AndCodesAreUnique()
    {
        var constants = CatalogueConstants().ToList();
        var definitionCodes = PermissionCatalog.All.Select(d => d.Code).ToList();

        var duplicateDefinitions = definitionCodes.GroupBy(c => c, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicateDefinitions);

        var duplicateConstants = constants.GroupBy(c => c.Value, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Name))}");
        Assert.Empty(duplicateConstants);

        var withoutDefinition = constants.Where(c => PermissionCatalog.Find(c.Value) is null).Select(c => c.Name);
        Assert.Empty(withoutDefinition);

        var definitionWithoutConstant = definitionCodes.Except(constants.Select(c => c.Value), StringComparer.Ordinal);
        Assert.Empty(definitionWithoutConstant);
    }

    [Fact]
    public void Seeder_WritesExactlyTheAssignableCatalogueCodes()
    {
        var seeded = PermissionSeeder.GetAllPermissions();
        var expected = PermissionCatalog.All.Where(d => d.Kind == PermissionKind.Assignable).ToList();

        Assert.Equal(
            expected.Select(d => (d.Code, d.Module, d.Description)),
            seeded.Select(p => (p.Code, p.Module, p.Description)));
        Assert.Contains(seeded, p => p.Code == PermissionCatalog.SuperAdmin);
        Assert.All(expected, d => Assert.False(string.IsNullOrWhiteSpace(d.Module), d.Code));
        Assert.All(expected, d => Assert.False(string.IsNullOrWhiteSpace(d.Description), d.Code));
        Assert.Empty(PermissionCatalog.RetiredCodes.Intersect(seeded.Select(p => p.Code)));
    }

    [Fact]
    public void AutoGrants_UseCatalogueCodes_AndEveryAutoGrantedCodeIsGranted()
    {
        var granted = ModuleAutoGrants.ByModule.Values.SelectMany(v => v).ToHashSet(StringComparer.Ordinal);
        foreach (var code in granted)
        {
            var definition = PermissionCatalog.Find(code);
            Assert.True(definition is { Kind: PermissionKind.AutoGranted or PermissionKind.Assignable },
                $"{code} is auto-granted but is {definition?.Kind.ToString() ?? "not in the catalogue"}.");
        }

        var autoGrantedKind = PermissionCatalog.All.Where(d => d.Kind == PermissionKind.AutoGranted).Select(d => d.Code);
        Assert.All(autoGrantedKind, code => Assert.Contains(code, granted));
    }

    [Fact]
    public void DerivedTriggers_UseCatalogueCodes_AndDerivedCodesAreMarked()
    {
        var triggers = DerivedPermissions.InboxTriggers.Concat(DerivedPermissions.NotificationTriggers);
        foreach (var code in triggers)
        {
            var definition = PermissionCatalog.Find(code);
            Assert.True(definition is not null && definition.Kind != PermissionKind.Retired,
                $"{code} is a derived-permission trigger but is {definition?.Kind.ToString() ?? "not in the catalogue"}.");
        }

        Assert.Equal(PermissionKind.Derived, PermissionCatalog.Find(PermissionCatalog.Inbox.Read)!.Kind);
        Assert.Equal(PermissionKind.Derived, PermissionCatalog.Find(PermissionCatalog.Notifications.Read)!.Kind);
    }

    [Fact]
    public void SeededRoleCodeLists_OnlyUseAssignableCodes()
    {
        var lists = new Dictionary<string, IEnumerable<string>>
        {
            ["RoleTemplateSeeder.HrManager"] = RoleTemplateSeeder.HrManagerPermissionCodes,
            ["DapiOrgStructureData.HrManager"] = DapiOrgStructureData.HrManagerPermissionCodes,
            ["DapiOrgStructureData.GeneralManager"] = DapiOrgStructureData.GeneralManagerPermissionCodes,
            ["DapiOrgStructureData.Manager"] = DapiOrgStructureData.ManagerPermissionCodes,
            ["DapiOrgStructureData.Employee"] = DapiOrgStructureData.EmployeePermissionCodes,
        };

        var bad = lists
            .SelectMany(l => l.Value.Select(code => (l.Key, code, PermissionCatalog.Find(code)?.Kind)))
            .Where(x => x.Kind != PermissionKind.Assignable)
            .Select(x => $"{x.Key}: {x.code} ({x.Kind?.ToString() ?? "unknown"})");
        Assert.Empty(bad);
    }

    [Fact]
    public void HrManagerTemplateJson_KeepsTheStoredText()
    {
        Assert.Equal(
            """["attendance:read","leave:approve","leave:manage","leave:read"]""",
            RoleTemplateSeeder.HrManagerPermissionCodesJson);
        Assert.Equal(
            RoleTemplateSeeder.HrManagerPermissionCodes,
            JsonSerializer.Deserialize<string[]>(RoleTemplateSeeder.HrManagerPermissionCodesJson));
    }

    private static IEnumerable<(string Name, string Value)> CatalogueConstants()
    {
        var types = new Stack<Type>();
        types.Push(typeof(PermissionCatalog));
        while (types.Count > 0)
        {
            var type = types.Pop();
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                types.Push(nested);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string))
                    yield return ($"{type.Name}.{field.Name}", (string)field.GetRawConstantValue()!);
            }
        }
    }

    /// <summary>Code lines of src/**/*.cs, without comment lines, migrations, build output or the catalogue.</summary>
    private static IEnumerable<(string File, int Line, string Code)> SourceCodeLines()
    {
        var src = FindSrcDirectory();
        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is "bin" or "obj" or "Migrations") || Path.GetFileName(path) == CatalogueFileName)
                continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                    continue;
                yield return (relative, i + 1, lines[i]);
            }
        }
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ONEVO.Api")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("src not found"), "src");
    }
}
