using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptions;

public class GetExceptionsQueryHandler : IRequestHandler<GetExceptionsQuery, Result<PagedResult<ExceptionDto>>>
{
    private readonly IExceptionRepository _exceptions;
    private readonly ITenantContext _tenantContext;

    public GetExceptionsQueryHandler(IExceptionRepository exceptions, ITenantContext tenantContext)
    {
        _exceptions = exceptions;
        _tenantContext = tenantContext;
    }

    public async Task<Result<PagedResult<ExceptionDto>>> Handle(GetExceptionsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId == Guid.Empty)
            return Result<PagedResult<ExceptionDto>>.Failure("Tenant context is required.", 401);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var tenantId = _tenantContext.TenantId;

        var total = await _exceptions.GetListTotalCountAsync(tenantId, request.Status, request.Type, ct);
        var items = await _exceptions.GetListAsync(tenantId, request.Status, request.Type, page, pageSize, ct);

        var dtos = items.Select(e => new ExceptionDto(
            e.Id, e.EmployeeId, e.Type.ToString(), e.Status.ToString(), e.Title, e.Description,
            e.DetectedAt, e.AcknowledgedAt, e.ResolvedAt, e.EscalatedAt)).ToList();

        return Result<PagedResult<ExceptionDto>>.Success(new PagedResult<ExceptionDto>(dtos, page, pageSize, total));
    }
}
