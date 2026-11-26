using System.Reflection;
using System.Text.RegularExpressions;
using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// DEC-113: on the tenant side only security, permission and sensitive-data changes are audited.
/// Every command in the namespaces below changes one of those, so it must implement
/// IAuditedCommand&lt;TResponse&gt; (AuditingBehavior then writes the tenant audit_logs row in the
/// same transaction) or be listed in <see cref="Exclusions"/> with a reviewed reason.
/// Everything else (leave, inbox, attendance, work) is traced through correlation-id logs.
/// </summary>
public class TenantAuditArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(IAuditedCommand).Assembly;

    private const string Features = "ONEVO.Application.Features.";

    /// <summary>Whole namespaces: every command under them is a security or sensitive-data change.</summary>
    private static readonly string[] NamespacePrefixes =
    [
        Features + "Auth.UserAccounts.",
        Features + "Monitoring.Settings.",
    ];

    /// <summary>Single command namespaces picked out of otherwise non-audited modules.</summary>
    private static readonly string[] ExactNamespaces =
    [
        Features + "Auth.Login.Commands.ForcePasswordChange",
        Features + "CoreHr.Employee.Commands.UpdateBankDetails",
        Features + "CoreHr.Offboarding.Commands.CompleteOffboarding",
        Features + "CoreHr.Offboarding.Commands.ApproveBypassRequest",
        Features + "CoreHr.Onboarding.Commands.ApproveAccessGrantRequest",
    ];

    private static readonly Regex ClockInPolicyNamespace = new(
        @"^ONEVO\.Application\.Features\.TimeAttendance\.Commands\.[A-Za-z]+ClockInPolicy$",
        RegexOptions.Compiled);

    /// <summary>Commands in scope that are reviewed as not audited through AuditingBehavior.</summary>
    private static readonly IReadOnlyDictionary<string, string> Exclusions = new Dictionary<string, string>
    {
        ["ForcePasswordChangeCommand"] =
            "Anonymous first-login step: the user replaces their own temporary password with email + current "
            + "password, before any session exists. AuditingBehavior would treat the caller as a system caller "
            + "and write platform_audit_logs, including a failed row for every wrong password. No admin action "
            + "forces a password change today (the flag is only set when onboarding issues the temporary password).",
    };

    /// <summary>The commands DEC-113 / plan A1 expects to find, so a rename cannot empty the scope silently.</summary>
    private static readonly string[] ExpectedCommands =
    [
        "SuspendUserAccountCommand",
        "ReactivateUserAccountCommand",
        "SignOutUserAccountCommand",
        "RevokeUserAccountSessionCommand",
        "ForcePasswordChangeCommand",
        "ApproveAccessGrantRequestCommand",
        "UpdateBankDetailsCommand",
        "CompleteOffboardingCommand",
        "ApproveBypassRequestCommand",
        "UpdateMonitoringFeatureTogglesCommand",
        "CreateClockInPolicyCommand",
        "UpdateClockInPolicyCommand",
        "ArchiveClockInPolicyCommand",
        "RestoreClockInPolicyCommand",
    ];

    private static List<Type> InScopeCommands()
        => ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.Namespace is { } ns
                && (NamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal))
                    || ExactNamespaces.Contains(ns, StringComparer.Ordinal)
                    || ClockInPolicyNamespace.IsMatch(ns)))
            .ToList();

    [Fact]
    public void Every_tenant_security_command_is_audited_or_reviewed()
    {
        var inScope = InScopeCommands();

        var names = inScope.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var notFound = ExpectedCommands.Where(n => !names.Contains(n)).ToList();
        Assert.True(notFound.Count == 0,
            "Expected tenant security commands were not found (renamed or moved?): " + string.Join(", ", notFound));

        var missing = inScope
            .Where(t => !typeof(IAuditedCommand).IsAssignableFrom(t) && !Exclusions.ContainsKey(t.Name))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.True(missing.Count == 0,
            "Tenant security / permission / sensitive-data commands must implement IAuditedCommand<TResponse> "
            + "(or be added to Exclusions with a reason):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Exclusions_are_in_scope_and_not_audited()
    {
        var inScope = InScopeCommands();
        foreach (var excluded in Exclusions.Keys)
        {
            var type = Assert.Single(inScope, t => t.Name == excluded);
            Assert.False(typeof(IAuditedCommand).IsAssignableFrom(type),
                $"{excluded} is audited now: remove it from Exclusions.");
        }
    }

    [Fact]
    public void Monitoring_toggles_have_a_single_audit_writer()
    {
        // AuditingBehavior writes the row for UpdateMonitoringFeatureTogglesCommand; a second,
        // hand-written row from the handler would record every change twice.
        var handler = ApplicationAssembly.GetTypes().Single(t => t.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
            && i.GetGenericArguments()[0] == typeof(UpdateMonitoringFeatureTogglesCommand)));

        var dependsOnAuditLog = handler.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IAuditLogRepository));

        Assert.False(dependsOnAuditLog,
            $"{handler.Name} must not write audit_logs itself: the command is audited by AuditingBehavior.");
    }
}
