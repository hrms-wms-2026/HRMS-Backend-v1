using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Data;

namespace ONEVO.Api.Dashboard.Widgets;

/// <summary>
/// Base for providers that mirror one existing controller action: re-checks that action's gates (providers
/// bypass MVC filters), then sends the same MediatR query and maps the result exactly as the action does.
/// A handler failure keeps its status code and message, as the endpoint's Problem response would.
/// </summary>
public abstract class EndpointMirrorWidgetDataProvider(
    IMediator mediator,
    ICurrentUser currentUser,
    IModuleEntitlementService entitlements) : IWidgetDataProvider
{
    public abstract string WidgetKey { get; }

    protected abstract EndpointGate Gate { get; }

    protected IMediator Mediator => mediator;

    public async Task<Result<object>> GetAsync(CancellationToken ct)
    {
        if (!await Gate.AllowsAsync(currentUser, entitlements, ct))
            return Result<object>.Forbidden();

        return await LoadAsync(ct);
    }

    protected abstract Task<Result<object>> LoadAsync(CancellationToken ct);

    /// <summary>Same branching as the controllers: success -> body, failure -> (error, status ?? 400).</summary>
    protected static Result<object> Map<T>(Result<T> result, Func<T, object> toBody) =>
        result.IsSuccess
            ? Result<object>.Success(toBody(result.Value!))
            : Result<object>.Failure(result.Error ?? "Request failed.", result.StatusCode ?? 400, result.ErrorCode);
}
