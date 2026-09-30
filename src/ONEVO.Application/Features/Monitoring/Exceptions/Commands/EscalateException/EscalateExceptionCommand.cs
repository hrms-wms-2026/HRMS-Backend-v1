using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;

/// <summary>The reporting manager hands a case to HR, with a note on why.</summary>
public record EscalateExceptionCommand(Guid ExceptionId, string? Note = null) : IRequest<Result>;
