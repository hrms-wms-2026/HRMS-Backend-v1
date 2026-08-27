using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;

public sealed record AddPercentageLogReasonCommand(Guid LogId, string Reason) : IRequest<Result>;
