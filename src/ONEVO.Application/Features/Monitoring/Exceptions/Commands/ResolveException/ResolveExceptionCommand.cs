using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;

public record ResolveExceptionCommand(Guid ExceptionId, string? Note = null) : IRequest<Result>;
