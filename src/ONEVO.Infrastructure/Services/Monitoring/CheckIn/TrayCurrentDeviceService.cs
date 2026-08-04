using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.Monitoring.CheckIn;

public class TrayCurrentDeviceService : ITrayCurrentDevice
{
    private readonly IHttpContextAccessor _http;

    public TrayCurrentDeviceService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public bool IsAuthenticated =>
        User?.Identity?.IsAuthenticated == true
        && User.FindFirstValue("token_type") == "tray_device";

    public Guid DeviceRegistrationId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(User?.FindFirstValue("user_id"), out var id)
            ? id
            : Guid.Empty;

    public Guid TenantId =>
        Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id)
            ? id
            : Guid.Empty;
}
