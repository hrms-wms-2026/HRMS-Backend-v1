using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Leave.Calendar.Commands;

public sealed record ListLeaveHolidaysQuery(DateOnly From, DateOnly To)
    : IRequest<Result<IReadOnlyList<LeaveHolidayResponse>>>;

public sealed class ListLeaveHolidaysQueryHandler
    : IRequestHandler<ListLeaveHolidaysQuery, Result<IReadOnlyList<LeaveHolidayResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICalendarEventRepository _events;

    public ListLeaveHolidaysQueryHandler(ICurrentUser currentUser, ICalendarEventRepository events)
    {
        _currentUser = currentUser;
        _events = events;
    }

    public async Task<Result<IReadOnlyList<LeaveHolidayResponse>>> Handle(
        ListLeaveHolidaysQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveHolidayResponse>>.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveHolidayResponse>>.Forbidden("Tenant context missing.");
        if (query.To < query.From)
            return Result<IReadOnlyList<LeaveHolidayResponse>>.Failure("End date must be on or after start date.");

        var from = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var to = new DateTimeOffset(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var events = await _events.ListBySourceTypeInRangeAsync(
            _currentUser.TenantId, CalendarEventSourceTypes.Holiday, from, to, ct);
        return Result<IReadOnlyList<LeaveHolidayResponse>>.Success(
            events.Select(ToResponse).ToList());
    }

    public static LeaveHolidayResponse ToResponse(CalendarEvent calendarEvent)
    {
        var start = DateOnly.FromDateTime(calendarEvent.StartDate.UtcDateTime);
        var end = DateOnly.FromDateTime(calendarEvent.EndDate.UtcDateTime);
        if (calendarEvent.EndDate.TimeOfDay == TimeSpan.Zero && calendarEvent.EndDate > calendarEvent.StartDate)
            end = end.AddDays(-1);
        if (end < start) end = start;
        return new LeaveHolidayResponse(calendarEvent.Id, calendarEvent.Title, start, end);
    }
}

public sealed record CreateLeaveHolidayCommand(string Name, DateOnly Date, DateOnly? EndDate)
    : IRequest<Result<LeaveHolidayResponse>>;

public sealed class CreateLeaveHolidayCommandValidator : AbstractValidator<CreateLeaveHolidayCommand>
{
    public CreateLeaveHolidayCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.Date)
            .When(x => x.EndDate is not null)
            .WithMessage("Holiday end date must be on or after the start date.");
    }
}

public sealed class CreateLeaveHolidayCommandHandler
    : IRequestHandler<CreateLeaveHolidayCommand, Result<LeaveHolidayResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICalendarEventRepository _events;
    private readonly IUnitOfWork _unitOfWork;

    public CreateLeaveHolidayCommandHandler(
        ICurrentUser currentUser, ICalendarEventRepository events, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _events = events;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LeaveHolidayResponse>> Handle(CreateLeaveHolidayCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveHolidayResponse>.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveHolidayResponse>.Forbidden("Tenant context missing.");

        var end = command.EndDate ?? command.Date;
        var entity = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            Title = command.Name.Trim(),
            StartDate = new DateTimeOffset(command.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            EndDate = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            SourceType = CalendarEventSourceTypes.Holiday,
            IsAllDay = true,
            Timezone = "UTC",
            EventStatus = CalendarEventStatuses.Confirmed,
            Recurrence = CalendarRecurrences.None,
            CreatedById = _currentUser.UserId
        };
        await _events.AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<LeaveHolidayResponse>.Success(ListLeaveHolidaysQueryHandler.ToResponse(entity));
    }
}

public sealed record DeleteLeaveHolidayCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteLeaveHolidayCommandHandler : IRequestHandler<DeleteLeaveHolidayCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICalendarEventRepository _events;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteLeaveHolidayCommandHandler(
        ICurrentUser currentUser, ICalendarEventRepository events, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _events = events;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteLeaveHolidayCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var entity = await _events.GetTrackedByIdForTenantAsync(_currentUser.TenantId, command.Id, ct);
        if (entity is null || entity.SourceType != CalendarEventSourceTypes.Holiday)
            return Result.NotFound("That holiday was not found.");

        _events.Remove(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
