using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.StartOffboarding;

public class StartOffboardingCommandHandler(
    IEmployeeRepository employeeRepository,
    IOffboardingRecordRepository offboardingRecordRepository,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<StartOffboardingCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(StartOffboardingCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;

        var employee = await employeeRepository.GetTrackedByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<Guid>.NotFound("The employee could not be found.");

        if (employee.UserId == currentUser.UserId)
            return Result<Guid>.Forbidden("You cannot start offboarding on your own record.");

        var existingOpen = await offboardingRecordRepository.GetOpenByEmployeeIdAsync(tenantId, employee.Id, ct);
        if (existingOpen is not null)
            return Result<Guid>.Conflict("This employee already has an offboarding in progress.");

        var record = new OffboardingRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            Reason = request.Reason,
            LastWorkingDate = request.LastWorkingDate,
            KnowledgeRiskLevel = request.KnowledgeRiskLevel,
            RehireEligibility = request.RehireEligibility,
            Notes = request.Notes,
            Status = OffboardingRecordStatuses.Initiated,
            InitiatedById = currentUser.UserId,
            PreviousEmploymentStatusId = employee.EmploymentStatusId,
            CreatedAt = clock.UtcNow,
        };
        await offboardingRecordRepository.AddAsync(record, ct);

        employee.EmploymentStatusId = ONEVO.Domain.Lookups.EmploymentStatusIds.Offboarding;

        await offboardingRecordRepository.SaveChangesAsync(ct);

        return Result<Guid>.Success(record.Id);
    }
}
