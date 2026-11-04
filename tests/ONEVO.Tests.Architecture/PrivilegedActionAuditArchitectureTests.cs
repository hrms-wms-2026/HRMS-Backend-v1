using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using ONEVO.Api.Controllers.Admin.DevPlatform.Audit;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Every privileged command (Developer Platform, tenant role management) must leave an audit row.
/// AuditingBehavior only writes one for commands that implement the audited-command marker, so this
/// test is what makes coverage exact: a new command under these namespaces fails here until it either
/// implements the marker or is added to <see cref="ReviewedExclusions"/> with a reason.
/// </summary>
public sealed class PrivilegedActionAuditArchitectureTests
{
    private const string DevPlatformNamespace = "ONEVO.Application.Features.DevPlatform.";
    private const string TenantRolesNamespace = "ONEVO.Application.Features.Auth.Roles.";

    /// <summary>
    /// Commands under the audited namespaces that deliberately write no audit row. Each entry is a
    /// reviewed decision; keep the reason specific.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ReviewedExclusions = new Dictionary<string, string>
    {
        [DevPlatformNamespace + "PlatformAccess.Commands.AcceptPlatformManagerInvite.AcceptPlatformManagerInviteCommand"] =
            "Unauthenticated self-service by the invitee (invite token + new password); there is no platform " +
            "actor. The handler records it as platform_auth_events platform_manager_invite_accepted.",
        [DevPlatformNamespace + "SystemConfig.PaymentGateway.Commands.VerifyGatewayCredentials.VerifyGatewayCredentialsCommand"] =
            "Stateless check of caller-supplied credentials against the provider: writes and stores nothing.",
    };

    /// <summary>
    /// NOT reviewed exclusions: privileged commands that are waiting on a product decision before they
    /// can be audited. Their handlers change tracked entities but never call SaveChanges, so auditing
    /// them through AuditingBehavior (which saves) would also start persisting the change. Empty this
    /// list once that is decided; do not add to it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PendingDecision = new Dictionary<string, string>
    {
        [DevPlatformNamespace + "PlatformAccess.Commands.UpdatePlatformRolePermissions.UpdatePlatformRolePermissionsCommand"] =
            "Handler does not call SaveChanges.",
        [DevPlatformNamespace + "PlatformAccess.Commands.UpdatePlatformUserRoles.UpdatePlatformUserRolesCommand"] =
            "Handler does not call SaveChanges.",
        [DevPlatformNamespace + "PlatformAccess.Commands.RevokePlatformUserSession.RevokePlatformUserSessionCommand"] =
            "Handler does not call SaveChanges.",
        [DevPlatformNamespace + "PlatformAccess.Commands.RevokePlatformUserSessions.RevokePlatformUserSessionsCommand"] =
            "Handler does not call SaveChanges.",
    };

    private static bool IsExcluded(string fullName) =>
        ReviewedExclusions.ContainsKey(fullName) || PendingDecision.ContainsKey(fullName);

    private static IEnumerable<Type> InScopeCommands() =>
        typeof(AuditingBehavior<,>).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && typeof(IBaseRequest).IsAssignableFrom(t)
                        && t.Namespace is { } ns
                        && (ns.StartsWith(DevPlatformNamespace, StringComparison.Ordinal)
                            || ns.StartsWith(TenantRolesNamespace, StringComparison.Ordinal)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static Type ResponseTypeOf(Type command)
    {
        var generic = command.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));
        return generic?.GetGenericArguments()[0] ?? typeof(Unit);
    }

    [Fact]
    public void EveryPrivilegedCommand_IsAudited_OrIsAReviewedExclusion()
    {
        var missing = InScopeCommands()
            .Where(t => !typeof(IAuditedCommand).IsAssignableFrom(t) && !IsExcluded(t.FullName!))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(missing.Count == 0,
            "These commands write no audit row. Implement IPlatformAuditedCommand<TResponse> / " +
            "ITenantAuditedCommand<TResponse>, or add a reviewed exclusion with a reason:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void ReviewedExclusions_AreNotStale()
    {
        var inScope = InScopeCommands().ToDictionary(t => t.FullName!);
        var stale = ReviewedExclusions.Keys.Concat(PendingDecision.Keys)
            .Where(name => !inScope.TryGetValue(name, out var type) || typeof(IAuditedCommand).IsAssignableFrom(type))
            .ToList();

        Assert.True(stale.Count == 0,
            "Exclusions that no longer exist or are now audited (remove them):\n" + string.Join("\n", stale));
        Assert.All(ReviewedExclusions.Values.Concat(PendingDecision.Values),
            reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
        Assert.Empty(ReviewedExclusions.Keys.Intersect(PendingDecision.Keys));
        Assert.True(PendingDecision.Count <= 4, "PendingDecision may only shrink.");
    }

    [Fact]
    public void AuditedCommand_MarkerResponseType_MatchesTheRequestResponseType()
    {
        // AuditingBehavior<TRequest, TResponse> matches on IAuditedCommand<TResponse>. A marker declared
        // with a different response type compiles but is silently never audited.
        var mismatched = InScopeCommands()
            .Where(t => typeof(IAuditedCommand).IsAssignableFrom(t))
            .Where(t => !typeof(IAuditedCommand<>).MakeGenericType(ResponseTypeOf(t)).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(mismatched.Count == 0,
            "Audited-command marker response type does not match IRequest<T>:\n" + string.Join("\n", mismatched));
    }

    [Fact]
    public void DevPlatformCommands_UsePlatformScope_AndTenantRoleCommands_UseTenantScope()
    {
        var wrong = new List<string>();
        foreach (var t in InScopeCommands().Where(t => typeof(IAuditedCommand).IsAssignableFrom(t)))
        {
            var response = ResponseTypeOf(t);
            var platform = typeof(IPlatformAuditedCommand<>).MakeGenericType(response).IsAssignableFrom(t);
            var tenant = typeof(ITenantAuditedCommand<>).MakeGenericType(response).IsAssignableFrom(t);
            var expectPlatform = t.Namespace!.StartsWith(DevPlatformNamespace, StringComparison.Ordinal);

            if (platform == tenant || platform != expectPlatform)
                wrong.Add(t.FullName!);
        }

        Assert.True(wrong.Count == 0,
            "DevPlatform commands must be IPlatformAuditedCommand<>, Auth.Roles commands ITenantAuditedCommand<>:\n" +
            string.Join("\n", wrong));
    }

    [Fact]
    public void PlatformAuditLog_IsNotTenantOwned_SoItNeedsNoRlsPolicy()
    {
        Assert.False(typeof(ITenantOwnedEntity).IsAssignableFrom(typeof(PlatformAuditLog)));
    }

    [Fact]
    public void AuditingBehavior_RunsAfterAdminTenantContextAndValidation()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "ONEVO.Application", "DependencyInjection.cs"));

        var adminContext = source.IndexOf("typeof(AdminTenantContextBehavior<,>)", StringComparison.Ordinal);
        var validation = source.IndexOf("typeof(ValidationBehavior<,>)", StringComparison.Ordinal);
        var auditing = source.IndexOf("typeof(AuditingBehavior<,>)", StringComparison.Ordinal);

        Assert.True(adminContext >= 0 && validation >= 0 && auditing >= 0, "expected all three behaviors to be registered");
        Assert.True(auditing > adminContext && auditing > validation,
            "AuditingBehavior must be registered after AdminTenantContextBehavior and ValidationBehavior");
    }

    [Fact]
    public void PlatformAuditLogs_RuntimeRoleHasNoUpdateOrDelete()
    {
        var root = FindRepositoryRoot();
        var migration = Directory.GetFiles(Path.Combine(root, "src", "ONEVO.Infrastructure", "Migrations"), "*_AddPlatformAuditLogs.cs")
            .Single();
        var migrationSource = File.ReadAllText(migration);
        Assert.Contains("REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.platform_audit_logs FROM {RuntimeAppRole}", migrationSource, StringComparison.Ordinal);
        Assert.Contains("RuntimeAppRole = \"onevo_app\"", migrationSource, StringComparison.Ordinal);

        // local-bootstrap-roles.sql re-grants UPDATE/DELETE on every table when it re-runs; the
        // post-migration script must take them back.
        var postMigration = File.ReadAllText(Path.Combine(root, "ops", "postgres", "local-post-migration-grants.sql"));
        Assert.Contains("REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.platform_audit_logs FROM %I", postMigration, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformAuditLogController_IsAdminOnly_ReadOnly_AndRequiresAuditRead()
    {
        var type = typeof(PlatformAuditLogController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "AdminPolicy");

        var actions = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            Assert.Equal(PlatformPermissionCatalog.AuditRead, action.GetCustomAttribute<RequirePlatformPermissionAttribute>()?.Permission);
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            Assert.Equal(new[] { "GET" }, verbs);
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
