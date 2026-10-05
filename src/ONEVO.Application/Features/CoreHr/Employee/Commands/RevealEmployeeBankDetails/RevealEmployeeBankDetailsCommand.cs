using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevealEmployeeBankDetails;

public sealed record RevealEmployeeBankDetailsCommand(Guid EmployeeId)
    : IRequest<Result<EmployeeBankDetailsRevealResponse>>;
