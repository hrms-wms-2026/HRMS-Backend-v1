using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiOrgStructureSeeder
{
    private static async Task SeedNewAccountsAsync(
        ApplicationDbContext db,
        IPasswordHasher passwordHasher,
        Dictionary<string, Guid> departmentIdByCode,
        Dictionary<string, Guid> positionIdByCode,
        Dictionary<string, Guid> roleIdByName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var hire in DapiOrgStructureData.NewHires)
        {
            var userId = DeterministicGuid($"dapi-org:user:{hire.Key}");
            var employeeId = DeterministicGuid($"dapi-org:employee:{hire.Key}");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                user = new User
                {
                    Id = userId,
                    TenantId = DapiTenantId,
                    Email = hire.Email,
                    FirstName = hire.FirstName,
                    LastName = hire.LastName,
                    PasswordHash = passwordHasher.Hash(NewHirePassword),
                    IsActive = true,
                    EmailVerified = true,
                    MustChangePassword = false,
                    PasswordSetByAdmin = false,
                    CreatedAt = now,
                    CreatedById = DapiOwnerUserId
                };
                db.Users.Add(user);
            }
            else
            {
                user.Email = hire.Email;
                user.FirstName = hire.FirstName;
                user.LastName = hire.LastName;
                user.IsActive = true;
                user.UpdatedAt = now;
            }

            var departmentId = departmentIdByCode[hire.DepartmentCode];
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (employee is null)
            {
                employee = new Employee
                {
                    Id = employeeId,
                    TenantId = DapiTenantId,
                    UserId = userId,
                    EmployeeNumber = hire.EmployeeNumber,
                    FirstName = hire.FirstName,
                    LastName = hire.LastName,
                    Email = hire.Email,
                    Phone = hire.Phone,
                    DateOfBirth = hire.DateOfBirth,
                    Gender = hire.Gender,
                    DepartmentId = departmentId,
                    LegalEntityId = DapiLegalEntityId,
                    EmploymentTypeId = DefaultEmploymentTypeId,
                    EmploymentStatusId = DefaultEmploymentStatusId,
                    WorkModeId = DefaultWorkModeId,
                    HireDate = hire.HireDate,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                };
                db.Employees.Add(employee);
            }
            else
            {
                employee.Phone = hire.Phone;
                employee.DateOfBirth = hire.DateOfBirth;
                employee.Gender = hire.Gender;
                employee.DepartmentId = departmentId;
                employee.LegalEntityId = DapiLegalEntityId;
                employee.UpdatedAt = now;
            }

            await SeedEmployeeAddressAsync(db, hire.Key, employeeId, hire.AddressLine, hire.City, now, ct);
            await SeedEmergencyContactAsync(
                db, hire.Key, employeeId, hire.EmergencyContactName,
                hire.EmergencyContactRelationship, hire.EmergencyContactPhone, ct);

            var positionId = positionIdByCode[hire.PositionCode];
            await SeedPositionAssignmentAsync(db, $"newhire:{hire.Key}", employeeId, positionId, hire.HireDate, ct);

            await AssignRoleAsync(db, userId, roleIdByName[hire.RoleName], ct);
            await AssignRoleAsync(db, userId, roleIdByName[DapiOrgStructureData.RoleEmployee], ct);
        }
    }

    private static async Task BackfillOwnerAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> departmentIdByCode,
        Dictionary<string, Guid> positionIdByCode,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var owner = await db.Employees.FirstOrDefaultAsync(e => e.UserId == DapiOwnerUserId, ct);
        if (owner is null)
        {
            return;
        }

        owner.DepartmentId = departmentIdByCode[DapiOrgStructureData.ExecDept];
        owner.Phone ??= "+94771234001";
        owner.DateOfBirth ??= new DateOnly(1980, 6, 15);
        owner.Gender ??= "Male";
        owner.UpdatedAt = now;

        await SeedEmployeeAddressAsync(
            db, "owner", owner.Id, "1 Independence Avenue", "Colombo", now, ct);
        await SeedEmergencyContactAsync(
            db, "owner", owner.Id, "Anusha Dapi", "Spouse", "+94771234090", ct);

        await SeedPositionAssignmentAsync(
            db, "owner", owner.Id, positionIdByCode[DapiOrgStructureData.CeoPosition],
            new DateOnly(2023, 1, 1), ct);
        // Owner keeps the existing "Tenant Owner" role (full access) seeded by
        // DevSmokeTestTenantSeeder - no additional role assignment needed here.
    }

    private static async Task RestructureExistingEmployeesAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> departmentIdByCode,
        Dictionary<string, Guid> positionIdByCode,
        Dictionary<string, Guid> roleIdByName,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var managerRoleId = roleIdByName[DapiOrgStructureData.RoleManager];
        var employeeRoleId = roleIdByName[DapiOrgStructureData.RoleEmployee];

        foreach (var group in DapiOrgStructureData.TeamGroups)
        {
            var departmentId = departmentIdByCode[group.DeptCode];
            var leadPositionId = positionIdByCode[group.LeadPositionCode];
            var memberPositionId = positionIdByCode[group.MemberPositionCode];

            await PlaceExistingEmployeeAsync(
                db, group.LeadPersonKey, departmentId, leadPositionId, now, ct);
            await AssignRoleAsync(
                db, WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:user:{group.LeadPersonKey}"),
                managerRoleId, ct);

            foreach (var memberKey in group.MemberPersonKeys)
            {
                await PlaceExistingEmployeeAsync(
                    db, memberKey, departmentId, memberPositionId, now, ct);
                await AssignRoleAsync(
                    db, WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:user:{memberKey}"),
                    employeeRoleId, ct);
            }
        }
    }

    private static async Task PlaceExistingEmployeeAsync(
        ApplicationDbContext db,
        string personKey,
        Guid departmentId,
        Guid positionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var employeeId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:employee:{personKey}");
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
        {
            // WorkManagementDapiDemoSeeder must run first - nothing to restructure yet.
            return;
        }

        employee.DepartmentId = departmentId;
        employee.UpdatedAt = now;

        await SeedPositionAssignmentAsync(
            db, $"existing:{personKey}", employeeId, positionId, employee.HireDate, ct);
    }

    private static async Task SeedEmployeeAddressAsync(
        ApplicationDbContext db,
        string personKey,
        Guid employeeId,
        string addressLine,
        string city,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var id = DeterministicGuid($"dapi-org:address:{personKey}");
        var existing = await db.EmployeeAddresses.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (existing is not null)
        {
            return;
        }

        var addressJson = JsonSerializer.Serialize(new
        {
            line1 = addressLine,
            city,
            country = "LK"
        });

        db.EmployeeAddresses.Add(new EmployeeAddress
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            AddressType = "current",
            AddressJson = addressJson,
            IsPrimary = true,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
    }

    private static async Task SeedEmergencyContactAsync(
        ApplicationDbContext db,
        string personKey,
        Guid employeeId,
        string name,
        string relationship,
        string phone,
        CancellationToken ct)
    {
        var id = DeterministicGuid($"dapi-org:emergency-contact:{personKey}");
        var existing = await db.EmployeeEmergencyContacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is not null)
        {
            return;
        }

        db.EmployeeEmergencyContacts.Add(new EmployeeEmergencyContact
        {
            Id = id,
            TenantId = DapiTenantId,
            EmployeeId = employeeId,
            Name = name,
            Relationship = relationship,
            Phone = phone,
            IsPrimary = true,
            CreatedById = DapiOwnerUserId,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
