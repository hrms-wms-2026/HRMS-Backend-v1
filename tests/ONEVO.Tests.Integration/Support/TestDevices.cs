using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// The tray_device_registrations row behind a fixture agent device id. Since task 39e, the monitoring snapshots'
/// agent_device_id has a foreign key to tray_device_registrations, so a fixture that writes a device id must add
/// the device too.
/// </summary>
public static class TestDevices
{
    /// <summary>Adds a registered device of the employee's user to the context and returns its id. Call SaveChanges as usual.</summary>
    public static async Task<Guid> ForEmployeeAsync(ApplicationDbContext db, Guid tenantId, Guid employeeId)
    {
        var userId = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == employeeId)
            .Select(e => e.UserId)
            .SingleAsync();
        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            DeviceName = "Fixture laptop",
            DeviceOs = "Windows 11",
            DeviceFingerprint = $"fixture-{Guid.NewGuid():N}",
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow,
        };
        db.TrayDeviceRegistrations.Add(device);
        return device.Id;
    }
}
