using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ONEVO.Api.Filters;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Task 15 (deny by default). The API has a fallback authorization policy, but every controller
/// action must still state its authorization explicitly: [Authorize] (with or without a named
/// policy), [RequirePermission] / [RequirePlatformPermission], or [AllowAnonymous], after the
/// class and method attributes are combined. This replaces dev-env/scripts/find-unauthorized-endpoints.ps1
/// as the check that runs in CI.
///
/// The public surface is pinned too: adding or removing an [AllowAnonymous] action fails
/// <see cref="AnonymousActions_MatchTheReviewedPublicEndpointList"/> until docs/api/public-endpoints.md
/// and <see cref="ReviewedAnonymousActions"/> are updated together.
/// </summary>
public sealed class ControllerAuthorizationCoverageArchitectureTests
{
    private static readonly Assembly ApiAssembly =
        typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;

    /// <summary>Every [AllowAnonymous] action, as Controller.Action. Keep in step with docs/api/public-endpoints.md.</summary>
    private static readonly string[] ReviewedAnonymousActions =
    [
        "AdminAuthController.AcceptInvite",
        "AdminAuthController.ForgotPassword",
        "AdminAuthController.GoogleCallback",
        "AdminAuthController.GoogleConfig",
        "AdminAuthController.Login",
        "AdminAuthController.ResetPassword",
        "AdminAuthController.VerifyMfa",
        "AuthInvitationController.AcceptEmployeeInvitation",
        "AuthInvitationController.AcceptInvitationGoogle",
        "AuthInvitationController.AcceptInvitationPassword",
        "AuthInvitationController.GetInvitation",
        "AuthLoginController.Login",
        "AuthLoginController.LoginWithGoogle",
        "AuthLoginController.SelectWorkspace",
        "AuthMfaController.VerifyMfa",
        "AuthPasswordController.ForceChangePassword",
        "AuthPasswordController.ForgotPassword",
        "AuthPasswordController.ResetPassword",
        "AuthPendingLegalController.AcceptPendingLegalDocuments",
        "AuthSessionController.SessionExchange",
        "CalendarOAuthCallbackController.Callback",
        "LegalDocumentContentController.GetByVersion",
        "LegalDocumentContentController.GetCurrent",
        "MonitoringActivationController.Exchange",
        "MonitoringActivationController.Refresh",
        "MonitoringDeviceAuthorizationController.Start",
        "MonitoringDeviceAuthorizationController.Token",
        "StripeWebhookController.Handle",
        "StripeWebhookController.HandleByConfigId",
        "TrayReleasesController.Ingest",
        "TrayReleasesPublicController.Check",
        "TrayReleasesPublicController.Latest",
    ];

    [Fact]
    public void EveryControllerAction_DeclaresAnAuthorizationDecision()
    {
        var controllers = ControllerTypes(ApiAssembly).ToList();
        Assert.NotEmpty(controllers);

        var offenders = FindActionsWithoutAuthorization(controllers);

        Assert.True(
            offenders.Count == 0,
            "Controller actions with no [Authorize], [RequirePermission], named policy or [AllowAnonymous] " +
            "(the fallback policy would decide for them):" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void AnonymousActions_MatchTheReviewedPublicEndpointList()
    {
        var anonymous = FindAnonymousActions(ControllerTypes(ApiAssembly)).Order(StringComparer.Ordinal).ToList();
        var reviewed = ReviewedAnonymousActions.Order(StringComparer.Ordinal).ToList();

        var added = anonymous.Except(reviewed).ToList();
        var removed = reviewed.Except(anonymous).ToList();

        Assert.True(
            added.Count == 0 && removed.Count == 0,
            "The public ([AllowAnonymous]) surface changed. Review each endpoint's protection, then update " +
            "docs/api/public-endpoints.md and ReviewedAnonymousActions." + Environment.NewLine +
            "New anonymous: " + string.Join(", ", added) + Environment.NewLine +
            "No longer anonymous: " + string.Join(", ", removed));
    }

    // Proves the check is not vacuous: a controller that drops its attribute is reported.

    [Fact]
    public void Checker_ReportsAnAction_WhenItsControllerDropsAuthorize()
    {
        var offenders = FindActionsWithoutAuthorization([typeof(FixtureControllerWithoutAuthorize)]);

        Assert.Equal(
            [$"{nameof(FixtureControllerWithoutAuthorize)}.{nameof(FixtureControllerWithoutAuthorize.Get)}"],
            offenders);
    }

    [Fact]
    public void Checker_AcceptsClassLevelAuthorize_PermissionAttributes_AndAllowAnonymous()
    {
        var offenders = FindActionsWithoutAuthorization(
        [
            typeof(FixtureControllerWithAuthorize),
            typeof(FixtureControllerWithPolicy),
            typeof(FixtureControllerWithMethodAttributes),
            typeof(FixtureDerivedController),
        ]);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Checker_ReportsOnlyTheActionMissingItsAttribute()
    {
        var offenders = FindActionsWithoutAuthorization([typeof(FixtureControllerWithOneUnprotectedAction)]);

        Assert.Equal(
            [$"{nameof(FixtureControllerWithOneUnprotectedAction)}.{nameof(FixtureControllerWithOneUnprotectedAction.Unprotected)}"],
            offenders);
    }

    internal static IEnumerable<Type> ControllerTypes(Assembly assembly) =>
        assembly.GetTypes().Where(IsController);

    internal static List<string> FindActionsWithoutAuthorization(IEnumerable<Type> controllers) =>
        controllers
            .SelectMany(c => ActionMethods(c).Select(m => (Controller: c, Method: m)))
            .Where(x => !HasAuthorizationDecision(x.Controller, x.Method))
            .Select(x => $"{x.Controller.Name}.{x.Method.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> FindAnonymousActions(IEnumerable<Type> controllers) =>
        controllers
            .SelectMany(c => ActionMethods(c).Select(m => (Controller: c, Method: m)))
            .Where(x => IsAnonymous(x.Controller, x.Method))
            .Select(x => $"{x.Controller.Name}.{x.Method.Name}")
            .Distinct();

    private static bool IsController(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsPublic: true }
        && typeof(ControllerBase).IsAssignableFrom(type)
        && type.GetCustomAttribute<NonControllerAttribute>(inherit: true) is null;

    /// <summary>
    /// Every public instance method MVC treats as an action (conventionally routed or not):
    /// declared on the controller or a base controller of ours, not on ControllerBase/Controller/object,
    /// and not [NonAction]. Deliberately broader than "has [Http*]" so nothing slips through.
    /// </summary>
    private static IEnumerable<MethodInfo> ActionMethods(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName
                        && m.GetBaseDefinition().DeclaringType is { } origin
                        && origin != typeof(object)
                        && origin != typeof(ControllerBase)
                        && origin != typeof(Controller)
                        && m.GetCustomAttribute<NonActionAttribute>(inherit: true) is null);

    private static IEnumerable<object> CombinedAttributes(Type controller, MethodInfo method) =>
        controller.GetCustomAttributes(inherit: true).Concat(method.GetCustomAttributes(inherit: true));

    private static bool HasAuthorizationDecision(Type controller, MethodInfo method) =>
        CombinedAttributes(controller, method).Any(a =>
            a is IAuthorizeData            // [Authorize], [Authorize(Policy = "...")]
                or IAllowAnonymous         // [AllowAnonymous]
                or RequirePermissionAttribute
                or RequirePlatformPermissionAttribute);

    private static bool IsAnonymous(Type controller, MethodInfo method)
    {
        if (method.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any())
            return true;

        // Class-level [AllowAnonymous] applies unless the action itself demands authorization
        // (ASP.NET Core would still let [AllowAnonymous] win, so flag such actions as anonymous too).
        return controller.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
    }

    // ---- Fixture controllers (test assembly only; never routed) ----

    public sealed class FixtureControllerWithoutAuthorize : ControllerBase
    {
        [HttpGet("fixture/without-authorize")]
        public IActionResult Get() => Ok();
    }

    [Authorize]
    public sealed class FixtureControllerWithAuthorize : ControllerBase
    {
        [HttpGet("fixture/with-authorize")]
        public IActionResult Get() => Ok();
    }

    [Authorize(Policy = "TenantPolicy")]
    public sealed class FixtureControllerWithPolicy : ControllerBase
    {
        [HttpGet("fixture/with-policy")]
        public IActionResult Get() => Ok();
    }

    public sealed class FixtureControllerWithMethodAttributes : ControllerBase
    {
        [HttpGet("fixture/permission")]
        [RequirePermission("employees:read")]
        public IActionResult Permission() => Ok();

        [HttpGet("fixture/platform-permission")]
        [RequirePlatformPermission("system_config:manage")]
        public IActionResult PlatformPermission() => Ok();

        [HttpGet("fixture/anonymous")]
        [AllowAnonymous]
        public IActionResult Anonymous() => Ok();
    }

    [Authorize(Policy = "AdminPolicy")]
    public abstract class FixtureBaseController : ControllerBase;

    public sealed class FixtureDerivedController : FixtureBaseController
    {
        [HttpGet("fixture/derived")]
        public IActionResult Get() => Ok();
    }

    [Route("fixture/mixed")]
    public sealed class FixtureControllerWithOneUnprotectedAction : ControllerBase
    {
        [HttpGet("protected")]
        [Authorize]
        public IActionResult Protected() => Ok();

        [HttpGet("unprotected")]
        public IActionResult Unprotected() => Ok();
    }
}
