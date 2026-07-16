using Microsoft.AspNetCore.Http;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Identity;

public sealed class HttpContextTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextTenantContext(IHttpContextAccessor accessor)
        => _accessor = accessor;

    public Guid TenantId
    {
        get
        {
            var claim = _accessor.HttpContext?.User.FindFirst("tenant_id")?.Value;
            if (claim is null || !Guid.TryParse(claim, out var id))
                throw new InvalidOperationException(
                    "tenant_id claim is missing or invalid in the current request.");
            return id;
        }
    }

    public string? Slug => throw new NotImplementedException();
    public TenantStatus? Status => throw new NotImplementedException();
    public bool IsResolved => throw new NotImplementedException();
    public TenantContextMode ContextMode => throw new NotImplementedException();
}
