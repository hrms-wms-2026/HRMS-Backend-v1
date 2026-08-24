namespace ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;

/// <summary>
/// Result of IEmployeeAuthorityResolver.ResolveVisibilityAsync: the concrete set of employee ids
/// the actor may see/manage for the requested purpose, already tenant- and legal-entity-scoped
/// and permission-gated. Deliberately distinct from
/// ONEVO.Application.Features.CoreHr.Employee.Models.EmployeeVisibilityScope (which holds
/// unexpanded covered position/department id sets for ListEmployeesQueryHandler's own SQL
/// filtering) - this type is the generic, already-expanded, purpose-agnostic result the resolver
/// returns to any caller.
/// </summary>
public sealed record EmployeeAuthorityVisibilityScope(
    Guid ActorUserId,
    Guid LegalEntityId,
    bool IncludesSelf,
    IReadOnlyCollection<Guid> EmployeeIds);
