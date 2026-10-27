using ONEVO.Application.Features.Dashboard.Layout.DTOs;

namespace ONEVO.Application.Features.Dashboard.Layout.Data;

public static class WidgetDataStatus
{
    public const string Success = "success";
    public const string Error = "error";
    public const string NotLoaded = "not_loaded";
    public const string Client = "client";
}

/// <summary>Generic widget error codes; exception text never reaches the client.</summary>
public static class WidgetDataErrors
{
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Timeout = "PROVIDER_TIMEOUT";
    public const string Failed = "PROVIDER_FAILED";
    public const string NoProvider = "NO_PROVIDER";
}

/// <summary>
/// One widget's load result. FailureStatusCode is the HTTP status the mirrored endpoint would have
/// returned (provider Result failures), 504 on timeout, 500 on an exception; null otherwise.
/// </summary>
public sealed record WidgetDataOutcome(WidgetDataStateDto State, object? Data, int? FailureStatusCode);

public interface IWidgetDataOrchestrator
{
    /// <summary>Loads every distinct key concurrently (bounded); never throws for a provider failure.</summary>
    Task<IReadOnlyDictionary<string, (WidgetDataStateDto State, object? Data)>> LoadAsync(
        IReadOnlyList<string> widgetKeys, CancellationToken ct);

    /// <summary>Loads a single widget, keeping the failure status code for the refresh endpoint.</summary>
    Task<WidgetDataOutcome> LoadOneAsync(string widgetKey, CancellationToken ct);
}
