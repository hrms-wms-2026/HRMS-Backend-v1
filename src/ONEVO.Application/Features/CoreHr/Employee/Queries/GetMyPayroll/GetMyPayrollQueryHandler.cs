using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;

public class GetMyPayrollQueryHandler : IRequestHandler<GetMyPayrollQuery, Result<MyPayrollResponse>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;

    public GetMyPayrollQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeProfileRepository profile,
        IEncryptionService encryption,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _profile = profile;
        _encryption = encryption;
        _currentUser = currentUser;
    }

    public async Task<Result<MyPayrollResponse>> Handle(GetMyPayrollQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyPayrollResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<MyPayrollResponse>.NotFound("No employee record for the current user.");

        var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);
        var masked = bankDetail is null ? null : BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));

        return Result<MyPayrollResponse>.Success(new MyPayrollResponse(
            bankDetail is not null, bankDetail?.BankName, masked, bankDetail?.AccountType,
            _currentUser.HasPermission("employees:write")));
    }
}
