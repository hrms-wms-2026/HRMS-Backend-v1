using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Single chokepoint for the base-domain-to-tenant-host handoff. CreateAsync runs on the base host
/// once every login gate has cleared; it never signs anyone in. ConsumeAsync runs on the tenant
/// host's session-exchange endpoint, where the tenant is already resolved from the request host; it
/// performs the equivalent of the old direct-sign-in path (ILoginSessionMaterialFactory.PrepareAsync)
/// so the controller can hand the result to TenantAuthResponseWriter.SignInAsync unchanged.
/// </summary>
public interface ITenantSessionExchangeService
{
    Task<Result<LoginResponseDto>> CreateAsync(
        User user,
        Tenant tenant,
        string authOrigin,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);

    Task<Result<LoginResponseDto>> ConsumeAsync(
        string rawCode,
        Guid resolvedTenantId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
