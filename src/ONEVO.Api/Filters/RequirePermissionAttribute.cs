using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Api.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
        => Permission = permission;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();

        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!currentUser.HasPermission(Permission))
        {
            context.Result = new ObjectResult(new
            {
                type = "https://onevo.com/errors/forbidden",
                title = "Forbidden",
                status = 403,
                detail = $"Permission '{Permission}' required."
            })
            { StatusCode = 403 };
        }
    }
}
