using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.ClientLogs.Commands.RecordClientLog;

public sealed record RecordClientLogCommand(
    string AdminUserId,
    string AdminEmail,
    string Level,
    string Message,
    IReadOnlyDictionary<string, object?>? Context,
    DateTimeOffset ClientTimestamp) : IRequest<Result>;
