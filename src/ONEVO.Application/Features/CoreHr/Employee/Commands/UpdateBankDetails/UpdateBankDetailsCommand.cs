using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public record UpdateBankDetailsCommand(
    string BankName, string BranchName, string AccountHolderName,
    string AccountNumber, string AccountType, string? RoutingNumber) : IRequest<Result>, IAuditedCommand<Result>
{
    // Self-service: the handler edits the signed-in user's own employee record, so the row's actor
    // (user_id) identifies whose bank details changed. Record the fact only: never the bank name,
    // holder name, account or routing number.
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("employee.bank_details_updated", "employee", targetId: null);
}
