namespace ONEVO.Application.Common.Exceptions;

/// <summary>Thrown by an outbox handler when retrying can never succeed: the processor parks the
/// message as Failed immediately instead of backing off (the handler has already told whoever needs to act).</summary>
public sealed class OutboxPermanentFailureException(string message, Exception? innerException = null) : Exception(message, innerException);
