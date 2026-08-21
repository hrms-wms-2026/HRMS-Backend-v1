using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.AcknowledgeException;

public record AcknowledgeExceptionCommand(Guid ExceptionId) : IRequest<Result>;
