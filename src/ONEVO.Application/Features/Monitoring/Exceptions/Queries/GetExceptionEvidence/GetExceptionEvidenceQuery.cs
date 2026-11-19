using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Queries.GetExceptionEvidence;

public record GetExceptionEvidenceQuery(Guid ExceptionId) : IRequest<Result<ExceptionEvidenceDto>>;
