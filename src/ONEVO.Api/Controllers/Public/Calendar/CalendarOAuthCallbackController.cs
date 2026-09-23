using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

namespace ONEVO.Api.Controllers.Public.Calendar;

[ApiController]
[Route("api/v1/calendar/connections")]
[AllowAnonymous]
public class CalendarOAuthCallbackController(IMediator mediator) : ControllerBase
{
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? error, [FromQuery] string state, CancellationToken ct)
    {
        var result = await mediator.Send(new CompleteCalendarConnectionCommand(provider, code, state), ct);
        return result.IsSuccess
            ? Redirect(result.Value!)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
