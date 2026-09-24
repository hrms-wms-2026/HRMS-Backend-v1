using MediatR;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed class RecordClientLogCommandHandler : IRequestHandler<RecordClientLogCommand, Result>
{
    private readonly ILogger<RecordClientLogCommandHandler> _logger;

    public RecordClientLogCommandHandler(ILogger<RecordClientLogCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task<Result> Handle(RecordClientLogCommand request, CancellationToken ct)
    {
        var logLevel = request.Level.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Error
            : LogLevel.Warning;

        _logger.Log(
            logLevel,
            "Client log reported. AdminUserId={AdminUserId} AdminEmail={AdminEmail} ClientTimestamp={ClientTimestamp} Message={Message} Context={@Context}",
            request.AdminUserId,
            request.AdminEmail,
            request.ClientTimestamp,
            request.Message,
            request.Context);

        return Task.FromResult(Result.Success());
    }
}
