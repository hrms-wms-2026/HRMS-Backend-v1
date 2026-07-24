namespace ONEVO.Application.Common.ServiceInterfaces;

public interface INetworkEvidenceHasher
{
    string? Protect(Guid tenantId, string? locallyHashedIdentifier);
}
