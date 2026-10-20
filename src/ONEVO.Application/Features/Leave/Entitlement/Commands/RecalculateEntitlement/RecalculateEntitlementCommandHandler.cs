using MediatR;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.DTOs.Responses;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.Mappers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.OrgStructure.Mappers;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;

public class RecalculateEntitlementCommandHandler
    : IRequestHandler<RecalculateEntitlementCommand, Result<LeaveEntitlementResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILeaveEntitlementRepository _entitlements;
    private readonly IEmployeeRepository _employees;
    private readonly ILeavePolicyRepository _policies;
    private readonly LeaveEntitlementCalculator _calculator;

    public RecalculateEntitlementCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ILeaveEntitlementRepository entitlements,
        IEmployeeRepository employees,
        ILeavePolicyRepository policies,
        LeaveEntitlementCalculator calculator)
    {
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _entitlements = entitlements;
        _employees = employees;
        _policies = policies;
        _calculator = calculator;
    }

    public Task<Result<LeaveEntitlementResponse>> Handle(
        RecalculateEntitlementCommand request, CancellationToken ct)
    {
        throw new NotImplementedException("Leave cleanup Part 2 Task 4");
    }
}
