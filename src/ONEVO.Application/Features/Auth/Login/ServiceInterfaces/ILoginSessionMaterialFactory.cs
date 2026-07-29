using ONEVO.Application.Common.Models;

using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public interface ILoginSessionMaterialFactory
{
    Task<Result<LoginResponseDto>> PrepareAsync(
        User user,
        Tenant tenant,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default);
}
