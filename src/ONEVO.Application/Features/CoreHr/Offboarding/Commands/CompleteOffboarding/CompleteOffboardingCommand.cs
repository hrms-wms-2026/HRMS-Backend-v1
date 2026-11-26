using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

public sealed record CompleteOffboardingCommand(Guid EmployeeId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("offboarding.completed", "employee", EmployeeId);
}
