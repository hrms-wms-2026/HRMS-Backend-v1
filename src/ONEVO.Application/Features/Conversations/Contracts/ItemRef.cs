namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>Addresses one inbox item across modules: ("work", "approval", requestId),
/// ("work", "monitor_alert", alertId), later ("leave", "request", id) and so on.</summary>
public sealed record ItemRef(string Source, string Kind, Guid Id);
