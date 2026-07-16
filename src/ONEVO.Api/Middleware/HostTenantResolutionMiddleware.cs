using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Api.Middleware;

public sealed class HostTenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly string _rootDomain;

    private static readonly HashSet<string> AdminSubdomains =
        new(StringComparer.OrdinalIgnoreCase) { "admin", "console" };

    private static readonly HashSet<string> SystemSubdomains =
        new(StringComparer.OrdinalIgnoreCase) { "www", "assets" };

    private static readonly HashSet<string> RejectedSubdomains =
        new(StringComparer.OrdinalIgnoreCase) { "api" };

    private static readonly Regex SlugPattern =
        new(@"^[a-z0-9][a-z0-9\-]{1,61}[a-z0-9]$", RegexOptions.Compiled);

    public HostTenantResolutionMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _next = next;
        _cache = cache;
        _rootDomain = configuration["Tenancy:RootDomain"]
            ?? throw new InvalidOperationException("Tenancy:RootDomain is not configured.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var writableCtx = context.RequestServices
            .GetRequiredService<IWritableTenantContext>();
        var tenants = context.RequestServices
            .GetRequiredService<ITenantRepository>();

        var host = context.Request.Host.Host.ToLowerInvariant().Trim();

        if (host != _rootDomain && !host.EndsWith("." + _rootDomain, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (host == _rootDomain)
        {
            writableCtx.SetSystemMode();
            await _next(context);
            return;
        }

        var subdomain = host[..^(_rootDomain.Length + 1)];

        if (subdomain.Contains('.'))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (AdminSubdomains.Contains(subdomain))
        {
            writableCtx.SetAdminMode();
            await _next(context);
            return;
        }

        if (SystemSubdomains.Contains(subdomain))
        {
            writableCtx.SetSystemMode();
            await _next(context);
            return;
        }

        if (RejectedSubdomains.Contains(subdomain))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!SlugPattern.IsMatch(subdomain))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var cacheKey = $"tenant:slug:{subdomain}";

        // Sentinel: null entry = known-missing slug
        if (_cache.TryGetValue(cacheKey, out TenantRegistryEntry? entry))
        {
            if (entry is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        else
        {
            var tenant = await tenants.GetBySlugAsync(subdomain, context.RequestAborted);
            if (tenant is null)
            {
                _cache.Set(cacheKey, (TenantRegistryEntry?)null, TimeSpan.FromSeconds(30));
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            entry = new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null);
            _cache.Set(cacheKey, entry, TimeSpan.FromMinutes(2));
        }

        writableCtx.Resolve(entry!);
        await _next(context);
    }
}
