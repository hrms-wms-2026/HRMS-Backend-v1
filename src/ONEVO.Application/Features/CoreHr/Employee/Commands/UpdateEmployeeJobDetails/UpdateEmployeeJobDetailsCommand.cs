using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmployeeJobDetails;

public sealed record UpdateEmployeeJobDetailsCommand(
    Guid EmployeeId, string EmployeeNumber, string EmploymentTypeCode, Guid? WorkModeId)
    : IRequest<Result<Unit>>;
