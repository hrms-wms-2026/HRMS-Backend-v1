using System.Net;
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

public class AgentWorkLocationEvidence : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? PresenceSessionId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public IPAddress PublicIp { get; set; } = IPAddress.None;
    public IPAddress? LocalIp { get; set; }
    public string? WifiSsid { get; set; }
    public string? WifiBssidHash { get; set; }
    public string? GatewayMacHash { get; set; }
    public bool VpnDetected { get; set; }
    public string? CoarseLocationJson { get; set; }

    /// <summary>matched | mismatch | unknown | not_evaluated</summary>
    public string MatchStatus { get; set; } = "not_evaluated";

    /// <summary>high | medium | low | unknown</summary>
    public string Confidence { get; set; } = "unknown";

    /// <summary>company_office | remote_profile | none</summary>
    public string? MatchedLocationSource { get; set; }

    public Guid? MatchedLocationSourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
