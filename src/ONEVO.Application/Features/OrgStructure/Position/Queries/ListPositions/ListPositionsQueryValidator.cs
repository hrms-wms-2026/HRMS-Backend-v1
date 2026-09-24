using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListPositions;

public class ListPositionsQueryValidator : AbstractValidator<ListPositionsQuery>
{
    private static readonly string[] AllowedSortBy =
        ["name", "code", "department", "reportsto", "type", "capacity", "status", "createdat", "updatedat"];
    private static readonly string[] AllowedSortDirections = ["asc", "desc"];
    private const int MaxPageSize = 100;

    public ListPositionsQueryValidator()
    {
        RuleFor(x => x.LegalEntityId).NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Search).MaximumLength(100).WithMessage("Search cannot exceed 100 characters.");

        RuleFor(x => x.SortBy)
            .NotEmpty().WithMessage("SortBy is required.")
            .Must(sortBy => AllowedSortBy.Contains(sortBy.Trim().ToLowerInvariant()))
            .WithMessage("SortBy must be one of: name, code, department, reportsTo, type, capacity, status, createdAt, updatedAt.");

        RuleFor(x => x.SortDirection)
            .NotEmpty().WithMessage("SortDirection is required.")
            .Must(direction => AllowedSortDirections.Contains(direction.Trim().ToLowerInvariant()))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");

        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {MaxPageSize}.");
    }
}
