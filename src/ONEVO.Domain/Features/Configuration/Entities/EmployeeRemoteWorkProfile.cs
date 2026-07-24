using System.Net;
using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Configuration.Entities;

public class EmployeeRemoteWorkProfile : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>pending_capture | active | archived | rejected</summary>
    public string Status { get; set; } = "pending_capture";

    public DateTimeOffset CapturedAt { get; set; }
    public IPAddress? PublicIp { get; set; }
    public string? WifiSsid { get; set; }
    public string? WifiBssidHash { get; set; }
    public string? GatewayMacHash { get; set; }
    public bool VpnDetected { get; set; }
    public string? CoarseLocationJson { get; set; }
    public Guid? VerificationRecordId { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}
