using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

/// <summary>Shared mocks for widget provider tests: a signed-in tenant user with chosen permissions/modules.</summary>
internal sealed class WidgetProviderTestKit
{
    public static readonly Guid TenantId = Guid.NewGuid();

    public static readonly string[] WorkModules =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public Mock<IMediator> Mediator { get; } = new();
    public Mock<ICurrentUser> User { get; } = new();
    public Mock<IModuleEntitlementService> Entitlements { get; } = new();

    public WidgetProviderTestKit(string[]? permissions = null, string[]? activeModules = null)
    {
        var granted = permissions ?? [];
        User.SetupGet(u => u.IsAuthenticated).Returns(true);
        User.SetupGet(u => u.TenantId).Returns(TenantId);
        User.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        User.SetupGet(u => u.Permissions).Returns(granted);
        User.Setup(u => u.HasPermission(It.IsAny<string>())).Returns((string p) => granted.Contains(p));
        Entitlements.Setup(e => e.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeModules ?? []);
    }

    /// <summary>The provider's payload is the same type and serializes to the same JSON as the endpoint's 200 body.</summary>
    public static void AssertSameBody(IActionResult endpoint, Result<object> provider)
    {
        var ok = Assert.IsType<OkObjectResult>(endpoint);
        Assert.True(provider.IsSuccess, provider.Error);
        Assert.NotNull(ok.Value);
        Assert.Equal(ok.Value!.GetType(), provider.Value!.GetType());
        Assert.Equal(JsonSerializer.Serialize(ok.Value, ok.Value.GetType(), Web),
            JsonSerializer.Serialize(provider.Value, provider.Value.GetType(), Web));
    }

    public static void AssertForbidden(Result<object> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }
}
