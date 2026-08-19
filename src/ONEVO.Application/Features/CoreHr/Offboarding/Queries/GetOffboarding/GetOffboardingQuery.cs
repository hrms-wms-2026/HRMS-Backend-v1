using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.GetOffboarding;

public sealed record GetOffboardingQuery(Guid EmployeeId) : IRequest<Result<OffboardingRecordResponse?>>;
