using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.CheckEmployeeNumberAvailability;

public sealed record EmployeeNumberAvailabilityResponse(string EmployeeNumber, bool Available);

public sealed record CheckEmployeeNumberAvailabilityQuery(string? EmployeeNumber)
    : IRequest<Result<EmployeeNumberAvailabilityResponse>>;
