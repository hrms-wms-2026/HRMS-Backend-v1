using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.OrgStructure;

/// <summary>
/// Single source of truth for whether a caller has tenant-wide "management access" to
/// legal entities (consumed by ListAccessibleAsync/GetAccessibleByIdAsync on
/// ILegalEntityRepository). Deliberately checks legal_entity:update/delete, not
/// org:manage - org:manage still gates Department/Position management and general org
/// navigation, but must not by itself unlock every company in the tenant for the
/// selector or General Settings.
/// </summary>
public static class LegalEntityAccessPolicy
{
    public static bool HasManagementAccess(ICurrentUser currentUser)
        => currentUser.HasPermission("legal_entity:update") || currentUser.HasPermission("legal_entity:delete");
}
