using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ONEVO.Api.Filters;

/// <summary>
/// Turns HTTPS response compression off for an endpoint whose body carries a secret (a token or
/// an activation code). Compressing a secret next to attacker-influenced text leaks it through
/// the compressed length (BREACH), so these few small responses stay uncompressed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoResponseCompressionAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Features.Get<IHttpsCompressionFeature>() is { } feature)
            feature.Mode = HttpsCompressionMode.DoNotCompress;
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
