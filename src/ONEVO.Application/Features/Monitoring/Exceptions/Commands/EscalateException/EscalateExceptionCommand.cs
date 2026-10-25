using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Commands.EscalateException;

/// <summary>The reporting manager hands a case on, with a note on why. <paramref name="NotifyUserIds"/>: the person picked
/// by hand, who gets the bell instead of the policy's next reviewers.</summary>
public record EscalateExceptionCommand(Guid ExceptionId, string? Note = null, IReadOnlyList<Guid>? NotifyUserIds = null) : IRequest<Result>;
