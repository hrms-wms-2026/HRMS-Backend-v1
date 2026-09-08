using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Options;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetTrayPresence;

public sealed class GetTrayPresenceQueryHandler
    : IRequestHandler<GetTrayPresenceQuery, Result<TrayPresenceResponseDto>>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly TrayPresenceOptions _options;

    public GetTrayPresenceQueryHandler(
        ITrayActivationRepository repository,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IOptions<TrayPresenceOptions> options)
    {
        _repository = repository;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<TrayPresenceResponseDto>> Handle(
        GetTrayPresenceQuery request,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var device = await _repository.FindLatestActiveDeviceForUserAsync(
            _currentUser.UserId,
            _currentUser.TenantId,
            ct);
        var connected = device?.LastSeenAt is { } lastSeen
            && lastSeen > now.AddSeconds(-_options.GracePeriodSeconds);
        var required = string.Equals(_options.Mode, "Enforce", StringComparison.OrdinalIgnoreCase);
        var validUntil = device?.LastSeenAt?.AddSeconds(_options.GracePeriodSeconds);
        var responseDevice = connected && device is not null && device.LastSeenAt is { } seen
            ? new TrayPresenceDeviceDto(device.Id, device.DeviceName, seen)
            : null;

        return Result<TrayPresenceResponseDto>.Success(
            new TrayPresenceResponseDto(
                required,
                connected,
                _options.GracePeriodSeconds,
                now,
                validUntil,
                responseDevice));
    }
}
