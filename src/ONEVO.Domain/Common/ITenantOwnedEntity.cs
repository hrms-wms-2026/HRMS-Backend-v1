namespace ONEVO.Domain.Common;

public interface ITenantOwnedEntity
{
    Guid TenantId { get; }
}
