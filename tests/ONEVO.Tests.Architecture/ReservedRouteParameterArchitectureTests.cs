using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// "action", "area", "controller", "handler" and "page" are reserved MVC route values: a route parameter with one of
/// those names is matched against the endpoint's own route values, so e.g. ".../actions/{action}" only matched the
/// literal method name "Execute" and every Inbox action button got an empty 404 (2026-10-15).
/// </summary>
public class ReservedRouteParameterArchitectureTests
{
    private static readonly string[] Reserved = ["action", "area", "controller", "handler", "page"];
    private static readonly Regex Parameter = new(@"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    [Fact]
    public void Controller_route_templates_never_use_a_reserved_route_parameter_name()
    {
        var apiAssembly = typeof(ONEVO.Api.Controllers.Tenant.Auth.AuthLoginController).Assembly;
        var offenders = new List<string>();
        foreach (var controller in apiAssembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var templates = controller.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => (Where: controller.Name, r.Template))
                .Concat(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes(inherit: true).OfType<IRouteTemplateProvider>()
                        .Select(r => (Where: $"{controller.Name}.{m.Name}", r.Template))));
            foreach (var (where, template) in templates)
            {
                if (template is null)
                    continue;
                foreach (Match match in Parameter.Matches(template))
                    if (Reserved.Contains(match.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
                        offenders.Add($"{where}: \"{template}\" uses reserved parameter {{{match.Groups[1].Value}}}");
            }
        }

        Assert.Empty(offenders);
    }
}
