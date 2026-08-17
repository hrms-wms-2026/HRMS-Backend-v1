using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;

public class UpdateBankDetailsCommandHandler : IRequestHandler<UpdateBankDetailsCommand, Result>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;

    public UpdateBankDetailsCommandHandler(
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

    public async Task<Result> Handle(UpdateBankDetailsCommand request, CancellationToken ct)
    {
        // Permission check is redundant with the controller's [RequirePermission("employees:write")]
        // but kept here too - handlers must not rely solely on controller attributes (backend-arch
        // §3.4: permission decisions are made server-side, and a handler can be invoked by other
        // future callers that skip the controller's attribute).
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");
        if (!_currentUser.HasPermission("employees:write"))
            return Result.Forbidden("You do not have permission to edit payroll details.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("No employee record for the current user.");

        var existing = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);
        var encryptedAccountNumber = _encryption.Encrypt(request.AccountNumber);

        if (existing is not null)
        {
            existing.BankName = request.BankName.Trim();
            existing.BranchName = request.BranchName.Trim();
            existing.AccountHolderName = request.AccountHolderName.Trim();
            existing.AccountNumberEncrypted = encryptedAccountNumber;
            existing.AccountType = request.AccountType.Trim();
            existing.RoutingNumber = request.RoutingNumber?.Trim();
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            await _profile.AddBankDetailAsync(new EmployeeBankDetail
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                BankName = request.BankName.Trim(),
                BranchName = request.BranchName.Trim(),
                AccountHolderName = request.AccountHolderName.Trim(),
                AccountNumberEncrypted = encryptedAccountNumber,
                AccountType = request.AccountType.Trim(),
                RoutingNumber = request.RoutingNumber?.Trim(),
                IsPrimary = true,
                CreatedById = _currentUser.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            }, ct);
        }

        await _profile.SaveChangesAsync(ct);
        return Result.Success();
    }
}
