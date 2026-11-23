namespace ONEVO.Api.Controllers.Webhooks;

/// <summary>
/// The one 400 body every rejected webhook gets, whatever the reason (bad gateway key, missing or
/// invalid signature, gateway not configured). The reason is logged server side under the same
/// correlation id, so callers learn nothing about which gateways exist or why a request failed.
/// </summary>
public sealed record WebhookRejection(string Type, string Title, int Status, string Detail, string CorrelationId)
{
    public const string GenericDetail = "The webhook request could not be accepted.";

    public static WebhookRejection For(string correlationId) => new(
        ONEVO.Api.Errors.ProblemTypes.For("bad-request"),
        "Bad Request",
        StatusCodes.Status400BadRequest,
        GenericDetail,
        correlationId);
}
