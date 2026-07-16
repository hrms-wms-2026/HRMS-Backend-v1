namespace ONEVO.Application.Features.DevPlatform.Tenancy.ServiceInterfaces;

public interface ITenantCacheInvalidator
{
    void InvalidateBySlug(string slug);
}
