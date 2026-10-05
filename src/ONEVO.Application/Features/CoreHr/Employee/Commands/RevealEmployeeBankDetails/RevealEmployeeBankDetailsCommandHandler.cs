using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using CoreHrEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.RevealEmployeeBankDetails;

public sealed class RevealEmployeeBankDetailsCommandHandler
    : IRequestHandler<RevealEmployeeBankDetailsCommand, Result<EmployeeBankDetailsRevealResponse>>
{
    private const string SensitivePermission = "employees:read:sensitive";

    private readonly CoreHrEmployeeRepository _employees;
    private readonly IEmployeeVisibilityScopeResolver _visibility;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public RevealEmployeeBankDetailsCommandHandler(
        CoreHrEmployeeRepository employees,
        IEmployeeVisibilityScopeResolver visibility,
        IEmployeeProfileRepository profile,
        IEncryptionService encryption,
        ICurrentUser currentUser,
        IAuditLogRepository auditLogs,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _employees = employees;
        _visibility = visibility;
        _profile = profile;
        _encryption = encryption;
        _currentUser = currentUser;
        _auditLogs = auditLogs;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EmployeeBankDetailsRevealResponse>> Handle(
        RevealEmployeeBankDetailsCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.HasPermission(SensitivePermission))
            return Result<EmployeeBankDetailsRevealResponse>.Forbidden(
                "You do not have permission to reveal employee bank details.");

        var tenantId = _currentUser.TenantId;
        var employee = await _employees.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<EmployeeBankDetailsRevealResponse>.NotFound(
                "The employee or selected organization record could not be found.");

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibility.ResolveAsync(tenantId, _currentUser.UserId, ct);

        var visible = await _employees.GetVisibleByIdAsync(tenantId, scope, request.EmployeeId, ct);
        if (visible is null)
            return Result<EmployeeBankDetailsRevealResponse>.Forbidden(
                "You do not have access to manage this employee.");

        var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, request.EmployeeId, ct);
        if (bankDetail is null)
            return Result<EmployeeBankDetailsRevealResponse>.NotFound(
                "No bank details are on file for this employee.");

        var accountNumber = _encryption.Decrypt(bankDetail.AccountNumberEncrypted);

        await _auditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = _currentUser.UserId,
            Action = "employee.bank_details_revealed",
            ResourceType = "Employee",
            ResourceId = request.EmployeeId,
            CreatedAt = _clock.UtcNow
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<EmployeeBankDetailsRevealResponse>.Success(
            new EmployeeBankDetailsRevealResponse(
                bankDetail.BankName,
                bankDetail.BranchName,
                bankDetail.AccountHolderName,
                accountNumber,
                bankDetail.AccountType,
                bankDetail.RoutingNumber));
    }
}
