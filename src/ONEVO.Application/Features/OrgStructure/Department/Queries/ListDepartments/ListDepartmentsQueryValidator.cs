using FluentValidation;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public class ListDepartmentsQueryValidator : AbstractValidator<ListDepartmentsQuery>
{
    private static readonly string[] AllowedViews = ["flat", "tree"];
    private static readonly string[] AllowedSortBy = ["name", "code", "createdat", "updatedat"];
    private static readonly string[] AllowedSortDirections = ["asc", "desc"];

    public ListDepartmentsQueryValidator()
    {
        RuleFor(x => x.LegalEntityId)
            .NotEmpty().WithMessage("Legal entity ID is required.");

        RuleFor(x => x.Search)
            .MaximumLength(100).WithMessage("Search cannot exceed 100 characters.");

        RuleFor(x => x.View)
            .NotEmpty().WithMessage("View is required.")
            .Must(view => AllowedViews.Contains(view.Trim().ToLowerInvariant()))
            .WithMessage("View must be 'flat' or 'tree'.");

        RuleFor(x => x.SortBy)
            .NotEmpty().WithMessage("SortBy is required.")
            .Must(sortBy => AllowedSortBy.Contains(sortBy.Trim().ToLowerInvariant()))
            .WithMessage("SortBy must be one of: name, code, createdAt, updatedAt.");

        RuleFor(x => x.SortDirection)
            .NotEmpty().WithMessage("SortDirection is required.")
            .Must(direction => AllowedSortDirections.Contains(direction.Trim().ToLowerInvariant()))
            .WithMessage("SortDirection must be 'asc' or 'desc'.");

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
    }
}
