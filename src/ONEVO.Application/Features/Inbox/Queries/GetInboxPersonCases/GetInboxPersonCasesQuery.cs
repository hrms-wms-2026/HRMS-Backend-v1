using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxPersonCases;

/// <summary>One local month (at most 32 days) of a person's alerts or requests, newest first.</summary>
public sealed record GetInboxPersonCasesQuery(Guid EmployeeId, string Category, DateTimeOffset From, DateTimeOffset To)
    : IRequest<Result<IReadOnlyList<InboxPersonCase>>>
{
    /// <summary>A local month is up to 31 days plus a daylight-saving hour, so allow a day of slack.</summary>
    public const int MaxRangeDays = 32;
    public const string BadCategoryMessage = "Category must be 'alert' or 'request'.";

    public static bool IsCategory(string category) => category is InboxCategories.Alert or InboxCategories.Request;

    /// <summary>Null when valid, otherwise the 400 message.</summary>
    public static string? Validate(string category, DateTimeOffset from, DateTimeOffset to)
    {
        if (!IsCategory(category))
            return BadCategoryMessage;
        if (to <= from || to - from > TimeSpan.FromDays(MaxRangeDays))
            return $"The range must be positive and at most {MaxRangeDays} days.";
        return null;
    }
}

public sealed class GetInboxPersonCasesQueryHandler : IRequestHandler<GetInboxPersonCasesQuery, Result<IReadOnlyList<InboxPersonCase>>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxPersonCaseService _cases;

    public GetInboxPersonCasesQueryHandler(InboxCallerResolver caller, IInboxPersonCaseService cases)
    {
        _caller = caller;
        _cases = cases;
    }

    public async Task<Result<IReadOnlyList<InboxPersonCase>>> Handle(GetInboxPersonCasesQuery request, CancellationToken ct)
    {
        if (GetInboxPersonCasesQuery.Validate(request.Category, request.From, request.To) is { } error)
            return Result<IReadOnlyList<InboxPersonCase>>.Failure(error);
        return await _caller.ResolveAsync(ct) is { } c
            ? Result<IReadOnlyList<InboxPersonCase>>.Success(
                await _cases.GetPersonCasesAsync(c.TenantId, c.EmployeeId, request.EmployeeId, request.Category, request.From, request.To, ct))
            : Result<IReadOnlyList<InboxPersonCase>>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
    }
}

/// <summary>Which months of <see cref="Year"/> (in the viewer's UTC offset) hold history, plus the hire date.</summary>
public sealed record GetInboxPersonCaseMonthsQuery(Guid EmployeeId, string Category, int Year, int OffsetMinutes)
    : IRequest<Result<InboxPersonCaseMonths>>;

public sealed class GetInboxPersonCaseMonthsQueryHandler : IRequestHandler<GetInboxPersonCaseMonthsQuery, Result<InboxPersonCaseMonths>>
{
    private readonly InboxCallerResolver _caller;
    private readonly IInboxPersonCaseService _cases;

    public GetInboxPersonCaseMonthsQueryHandler(InboxCallerResolver caller, IInboxPersonCaseService cases)
    {
        _caller = caller;
        _cases = cases;
    }

    public async Task<Result<InboxPersonCaseMonths>> Handle(GetInboxPersonCaseMonthsQuery request, CancellationToken ct)
    {
        if (!GetInboxPersonCasesQuery.IsCategory(request.Category))
            return Result<InboxPersonCaseMonths>.Failure(GetInboxPersonCasesQuery.BadCategoryMessage);
        if (request.Year is < 2000 or > 2100 || Math.Abs(request.OffsetMinutes) > 14 * 60)
            return Result<InboxPersonCaseMonths>.Failure("Year or offset out of range.");
        return await _caller.ResolveAsync(ct) is { } c
            ? Result<InboxPersonCaseMonths>.Success(await _cases.GetPersonCaseMonthsAsync(
                c.TenantId, c.EmployeeId, request.EmployeeId, request.Category, request.Year, request.OffsetMinutes, ct))
            : Result<InboxPersonCaseMonths>.Forbidden(InboxCallerResolver.NoEmployeeMessage);
    }
}
