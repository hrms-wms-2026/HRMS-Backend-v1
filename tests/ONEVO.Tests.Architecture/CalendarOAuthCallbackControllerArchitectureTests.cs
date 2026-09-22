using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using ONEVO.Api.Controllers.Public.Calendar;
using Xunit;

namespace ONEVO.Tests.Architecture;

public sealed class CalendarOAuthCallbackControllerArchitectureTests
{
    [Fact]
    public void CalendarOAuthCallbackController_IsAllowAnonymous_NotTenantPolicy()
    {
        var type = typeof(CalendarOAuthCallbackController);

        Assert.NotNull(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(type.GetCustomAttribute<AuthorizeAttribute>());
    }
}
