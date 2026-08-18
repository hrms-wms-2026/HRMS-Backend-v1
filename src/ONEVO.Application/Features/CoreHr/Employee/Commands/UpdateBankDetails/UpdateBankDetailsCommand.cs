using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public record UpdateBankDetailsCommand(
    string BankName, string BranchName, string AccountHolderName,
    string AccountNumber, string AccountType, string? RoutingNumber) : IRequest<Result>;
