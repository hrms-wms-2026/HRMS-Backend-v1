using System.Reflection;
using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Security audit finding F-07: every privileged admin command, and every tenant role or
/// permission command, leaves an audit row. A new command fails here until it implements
/// IAuditedCommand&lt;TResponse&gt; or is added, with a reviewed reason, to an exclusion list below.
/// </summary>
public class PlatformAuditLogArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IAuditedCommand).Assembly;

    /// <summary>Commands under DevPlatform or Auth.Roles that are reviewed as not privileged changes.</summary>
    private static readonly IReadOnlyDictionary<string, string> NamespaceExclusions = new Dictionary<string, string>
    {
        ["AcceptPlatformManagerInviteCommand"] =
            "Anonymous invitee setting their own password. Recorded as platform_auth_events platform_manager_invite_accepted.",
        ["VerifyGatewayCredentialsCommand"] =
            "Dry-run check of credentials typed into the create form. Saves nothing and carries raw secrets.",
    };

    /// <summary>Commands sent from admin controllers that are reviewed as not privileged changes.</summary>
    private static readonly IReadOnlyDictionary<string, string> AdminControllerExclusions = new Dictionary<string, string>
    {
        ["AdminLoginCommand"] = "Sign-in. Recorded in platform_auth_events.",
        ["AdminGoogleLoginCommand"] = "Sign-in. Recorded in platform_auth_events.",
        ["VerifyAdminMfaCommand"] = "Sign-in MFA step. Part of the auth flow (platform_auth_events).",
        ["EnableAdminMfaCommand"] = "The signed-in admin starts their own MFA enrolment. Auth flow.",
        ["ConfirmAdminMfaSetupCommand"] = "The signed-in admin confirms their own MFA enrolment. Auth flow.",
        ["RequestAdminPasswordResetCommand"] = "Anonymous forgot-password request. Recorded in platform_auth_events.",
        ["ResetAdminPasswordCommand"] = "Anonymous password reset by token. Recorded in platform_auth_events.",
        ["AcceptPlatformManagerInviteCommand"] = NamespaceExclusions["AcceptPlatformManagerInviteCommand"],
        ["VerifyGatewayCredentialsCommand"] = NamespaceExclusions["VerifyGatewayCredentialsCommand"],
        ["RecordClientLogCommand"] = "Browser error telemetry from the admin console. Not a change to platform data.",
    };

    private static readonly Regex ConstructedCommand = new(@"new\s+([A-Z][A-Za-z0-9]*Command)\s*\(", RegexOptions.Compiled);

    private static IEnumerable<Type> RequestTypes()
        => ApplicationAssembly.GetTypes().Where(t =>
            t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t));

    [Fact]
    public void Every_DevPlatform_and_tenant_role_command_is_audited_or_reviewed()
    {
        var inScope = RequestTypes()
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.Namespace is { } ns
                && (ns.StartsWith("ONEVO.Application.Features.DevPlatform.", StringComparison.Ordinal)
                    || ns.StartsWith("ONEVO.Application.Features.Auth.Roles.", StringComparison.Ordinal)))
            .ToList();

        Assert.True(inScope.Count > 50, $"Expected the DevPlatform command inventory, found {inScope.Count}.");

        var missing = inScope
            .Where(t => !typeof(IAuditedCommand).IsAssignableFrom(t) && !NamespaceExclusions.ContainsKey(t.Name))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.True(missing.Count == 0,
            "Commands that change platform or role state must implement IAuditedCommand<TResponse> "
            + "(or be added to NamespaceExclusions with a reason):\n" + string.Join('\n', missing));

        foreach (var excluded in NamespaceExclusions.Keys)
            Assert.Contains(inScope, t => t.Name == excluded);
    }

    [Fact]
    public void Every_command_sent_from_an_admin_controller_is_audited_or_reviewed()
    {
        var controllerRoot = Path.Combine(Root(), "src", "ONEVO.Api", "Controllers", "Admin");
        var sent = Directory.EnumerateFiles(controllerRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => ConstructedCommand.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(sent.Count > 50, $"Expected the admin command inventory, found {sent.Count}.");

        var byName = RequestTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
        var problems = new List<string>();
        foreach (var name in sent.OrderBy(n => n))
        {
            if (AdminControllerExclusions.ContainsKey(name))
                continue;
            if (!byName.TryGetValue(name, out var types))
            {
                problems.Add($"{name}: not found as a MediatR request in ONEVO.Application");
                continue;
            }

            if (!types.All(t => typeof(IAuditedCommand).IsAssignableFrom(t)))
                problems.Add($"{name}: does not implement IAuditedCommand<TResponse>");
        }

        Assert.True(problems.Count == 0,
            "Admin write commands must be audited (or added to AdminControllerExclusions with a reason):\n"
            + string.Join('\n', problems));

        var stale = AdminControllerExclusions.Keys.Where(k => !sent.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Remove stale admin exclusions: " + string.Join(", ", stale));
    }

    [Fact]
    public void Audited_commands_use_the_generic_marker_with_their_own_response_type()
    {
        var problems = new List<string>();
        foreach (var type in ApplicationAssembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAuditedCommand).IsAssignableFrom(t)))
        {
            var audited = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuditedCommand<>))
                .ToList();
            if (audited.Count != 1)
            {
                problems.Add($"{type.Name}: implements IAuditedCommand without exactly one IAuditedCommand<TResponse>");
                continue;
            }

            var request = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
            var responseType = request?.GetGenericArguments()[0]
                ?? (typeof(IRequest).IsAssignableFrom(type) ? typeof(Unit) : null);

            // AuditingBehavior matches on IAuditedCommand<TResponse>: a mismatch silently skips auditing.
            if (responseType != audited[0].GetGenericArguments()[0])
                problems.Add($"{type.Name}: IAuditedCommand<{audited[0].GetGenericArguments()[0].Name}> but the request returns {responseType?.Name ?? "nothing"}");
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void Platform_audit_log_is_platform_wide_and_needs_no_rls_policy()
    {
        Assert.False(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(PlatformAuditLog)));
        Assert.False(RlsTableRegistry.Tables.ContainsKey("platform_audit_logs"));
    }

    [Fact]
    public void Platform_audit_log_is_append_only()
    {
        var methods = typeof(IPlatformAuditLogRepository).GetMethods().Select(m => m.Name).ToList();
        Assert.All(methods, name => Assert.True(
            name.StartsWith("Add", StringComparison.Ordinal) || name.StartsWith("List", StringComparison.Ordinal),
            $"IPlatformAuditLogRepository.{name}: the audit log repository may only add and list rows."));

        var grants = File.ReadAllText(Path.Combine(Root(), "ops", "sqlserver", "post-migration-grants.sql"));
        Assert.Contains("DENY UPDATE, DELETE ON dbo.platform_audit_logs TO crownus_app;", grants, StringComparison.Ordinal);
    }

    // PA-SEC-002: every audit table is append-only for the runtime login, not only the platform one.
    [Theory]
    [InlineData("audit_logs")]
    [InlineData("platform_auth_events")]
    [InlineData("billing_audit_logs")]
    public void Other_audit_tables_are_append_only_for_the_runtime_login(string table)
    {
        var grants = File.ReadAllText(Path.Combine(Root(), "ops", "sqlserver", "post-migration-grants.sql"));

        Assert.Contains($"DENY UPDATE, DELETE ON dbo.{table} TO crownus_app;", grants, StringComparison.Ordinal);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ops")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
