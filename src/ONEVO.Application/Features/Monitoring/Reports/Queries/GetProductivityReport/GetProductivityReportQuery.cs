using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Reports.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;

public enum ProductivityReportScope { Employee, Department, Tenant }

public record GetProductivityReportQuery : IRequest<Result<ProductivityReportDto>>
{
    public ProductivityReportScope Scope { get; init; }
    public Guid? ScopeId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
}
