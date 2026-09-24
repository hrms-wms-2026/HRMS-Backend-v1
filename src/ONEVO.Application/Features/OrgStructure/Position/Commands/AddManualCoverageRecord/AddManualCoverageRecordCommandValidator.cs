using FluentValidation;
using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.Commands.AddManualCoverageRecord;

public class AddManualCoverageRecordCommandValidator : AbstractValidator<AddManualCoverageRecordCommand>
{
    public AddManualCoverageRecordCommandValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");
        RuleFor(x => x.PositionId).NotEmpty().WithMessage("Position ID is required.");

        RuleFor(x => x.CoveredTargetType)
            .NotEmpty().WithMessage("Covered target type is required.")
            .Must(type =>
                type == ManagementCoverageRecord.TargetPosition
                || type == ManagementCoverageRecord.TargetDepartment
                || type == ManagementCoverageRecord.TargetCompany)
            .WithMessage("Covered target type must be 'Position', 'Department', or 'Company'.");

        RuleFor(x => x.OwnerOrder)
            .GreaterThanOrEqualTo(1).WithMessage("Owner order must be at least 1.");

        RuleFor(x => x.CoveredPositionId)
            .NotNull().WithMessage("Covered position ID is required when covered target type is 'Position'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetPosition);
        RuleFor(x => x.CoveredDepartmentId)
            .Null().WithMessage("Covered department ID must not be set when covered target type is 'Position'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetPosition);

        RuleFor(x => x.CoveredDepartmentId)
            .NotNull().WithMessage("Covered department ID is required when covered target type is 'Department'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetDepartment);
        RuleFor(x => x.CoveredPositionId)
            .Null().WithMessage("Covered position ID must not be set when covered target type is 'Department'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetDepartment);

        RuleFor(x => x.CoveredPositionId)
            .Null().WithMessage("Covered position ID must not be set when covered target type is 'Company'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetCompany);
        RuleFor(x => x.CoveredDepartmentId)
            .Null().WithMessage("Covered department ID must not be set when covered target type is 'Company'.")
            .When(x => x.CoveredTargetType == ManagementCoverageRecord.TargetCompany);
    }
}
