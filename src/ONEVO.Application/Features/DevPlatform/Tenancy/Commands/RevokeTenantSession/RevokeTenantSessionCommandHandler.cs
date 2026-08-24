using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;

public sealed class RevokeTenantSessionCommandHandler : IRequestHandler<RevokeTenantSessionCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ISessionRepository _sessions;
    private readonly IUnitOfWork _unitOfWork;

    public RevokeTenantSessionCommandHandler(
        ITenantRepository tenants,
        ISessionRepository sessions,
        IUnitOfWork unitOfWork)
    {
        _tenants = tenants;
        _sessions = sessions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RevokeTenantSessionCommand request, CancellationToken ct)
    {
        if (await _tenants.GetByIdAsync(request.TenantId, ct) is null)
            return Result.NotFound("Tenant not found.");

        var session = await _sessions.GetByIdAsync(request.SessionId, ct);
        if (session is null || session.TenantId != request.TenantId)
            return Result.NotFound("Session not found for this tenant.");

        await _sessions.RevokeByIdAsync(request.SessionId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
