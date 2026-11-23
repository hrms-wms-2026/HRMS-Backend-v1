using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// The users row behind a fixture employee or another user-id column. Since task 39b, employees.user_id,
/// task_assignments.user_id and the other user-id columns have foreign keys to users, so a fixture that
/// writes a made-up user id must add the user too.
/// </summary>
public static class TestUsers
{
    public static User For(Guid tenantId, Guid userId, string firstName = "Test", string lastName = "User") => new()
    {
        Id = userId,
        TenantId = tenantId,
        Email = $"fixture-{userId:N}@example.test",
        PasswordHash = "x",
        FirstName = firstName,
        LastName = lastName,
        IsActive = true,
    };

    /// <summary>Adds the user unless the context already tracks it. Call SaveChanges as usual.</summary>
    public static Guid Add(ApplicationDbContext db, Guid tenantId, Guid userId, string firstName = "Test", string lastName = "User")
    {
        if (!db.Users.Local.Any(u => u.Id == userId))
            db.Users.Add(For(tenantId, userId, firstName, lastName));
        return userId;
    }

    /// <summary>A new user id with its users row added to the context.</summary>
    public static Guid New(ApplicationDbContext db, Guid tenantId, string firstName = "Test", string lastName = "User") =>
        Add(db, tenantId, Guid.NewGuid(), firstName, lastName);

    /// <summary>
    /// For fixture setup only, before SaveChanges: adds the users row of every employee the context is about to
    /// insert whose user is neither tracked nor stored yet.
    /// </summary>
    public static void AddForNewEmployees(ApplicationDbContext db)
    {
        var newEmployees = db.ChangeTracker.Entries<Employee>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();
        foreach (var employee in newEmployees)
        {
            if (db.Users.Local.Any(u => u.Id == employee.UserId)
                || db.Users.IgnoreQueryFilters().Any(u => u.Id == employee.UserId))
                continue;
            db.Users.Add(For(employee.TenantId, employee.UserId, employee.FirstName, employee.LastName));
        }
    }
}
