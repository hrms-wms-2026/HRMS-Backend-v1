using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;

namespace ONEVO.Api.Filters;

/// <summary>
/// Relationship-based gate for per-employee endpoints: the employee named by the route value must
/// be the caller or someone inside the caller's management coverage (IEmployeeViewAccess). There is
/// no permission code - managing the employee is the grant. When the route has no such value the
/// query string is read instead, for endpoints such as GET /monitoring/activity/snapshots?employeeId=
/// (a permission code alone let any holder read every employee in the tenant there). A missing or
/// unparseable id is refused, never treated as "no filter".
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireVisibleEmployeeAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _routeKey;

    public RequireVisibleEmployeeAttribute(string routeKey = "id")
        => _routeKey = routeKey;

    public string RouteKey => _routeKey;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is null || !currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var raw = context.RouteData.Values.TryGetValue(_routeKey, out var value)
            ? value?.ToString()
            : context.HttpContext.Request.Query.TryGetValue(_routeKey, out var query) && query.Count == 1
                ? query[0]
                : null;
        if (!Guid.TryParse(raw, out var employeeId))
        {
            context.Result = Forbidden();
            return;
        }

        var access = context.HttpContext.RequestServices.GetRequiredService<IEmployeeViewAccess>();
        if (!await access.CanViewAsync(employeeId, context.HttpContext.RequestAborted))
            context.Result = Forbidden();
    }

    private static ObjectResult Forbidden() => new(new
    {
        type = ONEVO.Api.Errors.ProblemTypes.For("forbidden"),
        title = "Forbidden",
        status = 403,
        detail = "You do not have access to this employee."
    })
    { StatusCode = StatusCodes.Status403Forbidden };
}
