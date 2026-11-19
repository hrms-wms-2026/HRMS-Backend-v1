using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.ResolveException;

/// <summary><see cref="Outcome"/>: excused or confirmed (default confirmed, what every resolve meant before outcomes existed).</summary>
public record ResolveExceptionCommand(Guid ExceptionId, string? Note = null, string? Outcome = null) : IRequest<Result>;
