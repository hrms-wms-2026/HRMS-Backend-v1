using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Dashboard.Widgets;

/// <summary>
/// The authorization gates of one existing controller action, read from its own attributes so a widget
/// provider re-checks exactly what the endpoint checks (providers bypass MVC filters). Evaluates
/// <see cref="RequirePermissionAttribute"/> (all), <see cref="RequireAnyPermissionAttribute"/> (one of each
/// set) and <see cref="RequireAnyModuleAttribute"/> (one active module of each set) with the same services
/// the filters use. Any other authorization filter or policy is rejected at construction, so a gate can never
/// be silently skipped.
/// </summary>
public sealed class EndpointGate
{
    /// <summary>The policy every tenant controller uses (authenticated tenant user); the dashboard endpoints share it.</summary>
    private const string TenantPolicy = "TenantPolicy";

    private EndpointGate(
        IReadOnlyList<string> permissions,
        IReadOnlyList<IReadOnlyList<string>> anyPermissionSets,
        IReadOnlyList<IReadOnlyList<string>> moduleKeySets)
    {
        Permissions = permissions;
        AnyPermissionSets = anyPermissionSets;
        ModuleKeySets = moduleKeySets;
    }

    public IReadOnlyList<string> Permissions { get; }
    public IReadOnlyList<IReadOnlyList<string>> AnyPermissionSets { get; }
    public IReadOnlyList<IReadOnlyList<string>> ModuleKeySets { get; }

    public static EndpointGate For<TController>(string actionName)
    {
        var controller = typeof(TController);
        var action = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == actionName);

        var attributes = controller.GetCustomAttributes(inherit: true)
            .Concat(action.GetCustomAttributes(inherit: true))
            .ToList();

        var permissions = new List<string>();
        var anyPermissions = new List<IReadOnlyList<string>>();
        var modules = new List<IReadOnlyList<string>>();

        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case RequirePermissionAttribute p:
                    permissions.Add(p.Permission);
                    break;
                case RequireAnyPermissionAttribute any:
                    anyPermissions.Add(any.Permissions);
                    break;
                case RequireAnyModuleAttribute m:
                    modules.Add(m.ModuleKeys);
                    break;
                case IAuthorizationFilter or IAsyncAuthorizationFilter:
                    throw new NotSupportedException(
                        $"{controller.Name}.{actionName} has authorization filter {attribute.GetType().Name}, which widget providers cannot mirror.");
                case IAllowAnonymous:
                    throw new NotSupportedException($"{controller.Name}.{actionName} allows anonymous access.");
                case IAuthorizeData authorize when authorize.Policy != TenantPolicy || !string.IsNullOrEmpty(authorize.Roles):
                    throw new NotSupportedException(
                        $"{controller.Name}.{actionName} requires policy '{authorize.Policy}' / roles '{authorize.Roles}', which widget providers cannot mirror.");
            }
        }

        return new EndpointGate(permissions, anyPermissions, modules);
    }

    public async Task<bool> AllowsAsync(ICurrentUser currentUser, IModuleEntitlementService entitlements, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (!Permissions.All(currentUser.HasPermission))
            return false;

        if (!AnyPermissionSets.All(set => set.Any(currentUser.HasPermission)))
            return false;

        if (ModuleKeySets.Count == 0)
            return true;

        var active = await entitlements.GetActiveModuleKeysForTenantAsync(currentUser.TenantId, ct);
        return ModuleKeySets.All(set => set.Count > 0
            && set.Any(required => active.Contains(required, StringComparer.OrdinalIgnoreCase)));
    }
}
