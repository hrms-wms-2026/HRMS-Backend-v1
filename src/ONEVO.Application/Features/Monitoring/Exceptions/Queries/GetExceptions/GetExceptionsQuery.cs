using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;

public record GetExceptionsQuery : IRequest<Result<PagedResult<ExceptionDto>>>
{
    public ExceptionStatus? Status { get; init; }
    public ExceptionType? Type { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
