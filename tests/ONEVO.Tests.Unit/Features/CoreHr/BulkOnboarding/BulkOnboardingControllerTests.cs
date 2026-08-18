using System.Reflection;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.BulkOnboarding;

public sealed class BulkOnboardingControllerTests
{
    [Fact]
    public void Upload_RequiresEmployeesWritePermission()
    {
        var method = typeof(BulkOnboardingController).GetMethod(nameof(BulkOnboardingController.Upload))!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }

    [Fact]
    public void Validate_RequiresEmployeesWritePermission()
    {
        var method = typeof(BulkOnboardingController).GetMethod(nameof(BulkOnboardingController.Validate))!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }

    [Fact]
    public void CreateDrafts_RequiresEmployeesWritePermission()
    {
        var method = typeof(BulkOnboardingController).GetMethod(nameof(BulkOnboardingController.CreateDrafts))!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }

    [Fact]
    public void Finalize_RequiresEmployeesWritePermission()
    {
        var method = typeof(BulkOnboardingController).GetMethod(nameof(BulkOnboardingController.Finalize))!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }

    [Fact]
    public void Finalize_IsIdempotent()
    {
        var method = typeof(BulkOnboardingController).GetMethod(nameof(BulkOnboardingController.Finalize))!;
        Assert.NotNull(method.GetCustomAttribute<IdempotentAttribute>());
    }
}
