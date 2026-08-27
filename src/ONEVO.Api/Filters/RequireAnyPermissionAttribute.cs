using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAnyPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        _permissions = permissions;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (_permissions.Any(currentUser.HasPermission))
            return;

        context.Result = new ObjectResult(new
        {
            type = "https://onevo.com/errors/forbidden",
            title = "Forbidden",
            status = 403,
            detail = $"One of these permissions is required: {string.Join(", ", _permissions)}."
        })
        { StatusCode = 403 };
    }
}
