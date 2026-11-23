using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Authorization;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class PermissionCatalogSynchronizerTests
{
    private static readonly DateOnly Day = new(2026, 11, 19);
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = new SystemDateTimeProvider();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(new Mock<IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }

    private static Task<PermissionSyncReport> SyncAsync(
        ApplicationDbContext db, PermissionLifecycleRules rules, params string[] catalogue) =>
        new PermissionCatalogSynchronizer(db, new SystemDateTimeProvider(), NullLogger.Instance)
            .SyncAsync(catalogue.Select(c => new PermissionDefinition(c, "core_hr", $"{c} description", PermissionKind.Assignable)).ToList(), rules);

    private static Permission AddPermission(ApplicationDbContext db, string code)
    {
        var permission = new Permission { Id = Guid.NewGuid(), Code = code, Description = code, Module = "core_hr" };
        db.Permissions.Add(permission);
        return permission;
    }

    private static Role AddRole(
        ApplicationDbContext db, Guid tenantId, string name, bool isSystem = false, Guid? templateId = null)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name, IsSystem = isSystem, SourceTemplateId = templateId,
        };
        db.Roles.Add(role);
        return role;
    }

    private static void Grant(ApplicationDbContext db, Role role, Permission permission) =>
        db.RolePermissions.Add(new RolePermission { TenantId = role.TenantId, RoleId = role.Id, PermissionId = permission.Id });

    private static UserPermissionOverride Override(ApplicationDbContext db, Guid tenantId, Permission permission, string type = "grant")
    {
        var userOverride = new UserPermissionOverride
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(), PermissionId = permission.Id,
            GrantType = type, Reason = "test", GrantedBy = Guid.NewGuid(),
        };
        db.UserPermissionOverrides.Add(userOverride);
        return userOverride;
    }

    private static PermissionLifecycleRules Renames(params (string From, string To)[] renames) =>
        new(renames.Select(r => new PermissionRename(r.From, r.To, Day, "test")).ToList(), [], []);

    private static async Task<List<string>> CodesHeldByRoleAsync(ApplicationDbContext db, Role role) =>
        await db.RolePermissions.Where(rp => rp.RoleId == role.Id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();

    [Fact]
    public async Task Rename_UpdatesTheRowInPlace_AndRoleGrantsAndOverridesFollowIt()
    {
        await using var db = NewDb();
        var old = AddPermission(db, "reports:view");
        var role = AddRole(db, TenantA, "Analyst");
        Grant(db, role, old);
        var userOverride = Override(db, TenantA, old);
        await db.SaveChangesAsync();

        var report = await SyncAsync(db, Renames(("reports:view", "reports:read")), "reports:read");

        report.Renamed.Should().Be(1);
        report.Added.Should().Be(0, "the renamed row is the new code; nothing new is inserted");
        var renamed = await db.Permissions.SingleAsync();
        renamed.Id.Should().Be(old.Id);
        renamed.Code.Should().Be("reports:read");
        (await CodesHeldByRoleAsync(db, role)).Should().Equal("reports:read");
        (await db.UserPermissionOverrides.SingleAsync()).Id.Should().Be(userOverride.Id);
        (await db.UserPermissionOverrides.SingleAsync()).PermissionId.Should().Be(old.Id);
        (await db.PermissionChanges.SingleAsync()).ChangeKey.Should().Be("rename:reports:view->reports:read");
    }

    [Fact]
    public async Task Rename_WhenTheNewCodeAlreadyExists_MergesGrantsWithoutDuplicates()
    {
        await using var db = NewDb();
        var old = AddPermission(db, "reports:view");
        var target = AddPermission(db, "reports:read");
        var both = AddRole(db, TenantA, "Holds both");
        var oldOnly = AddRole(db, TenantB, "Holds old only");
        Grant(db, both, old);
        Grant(db, both, target);
        Grant(db, oldOnly, old);
        var sharedUser = Guid.NewGuid();
        var moved = Override(db, TenantB, old);
        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            Id = Guid.NewGuid(), TenantId = TenantA, UserId = sharedUser, PermissionId = old.Id, GrantType = "grant", Reason = "x",
        });
        var kept = new UserPermissionOverride
        {
            Id = Guid.NewGuid(), TenantId = TenantA, UserId = sharedUser, PermissionId = target.Id, GrantType = "revoke", Reason = "x",
        };
        db.UserPermissionOverrides.Add(kept);
        await db.SaveChangesAsync();

        await SyncAsync(db, Renames(("reports:view", "reports:read")), "reports:read");

        (await db.Permissions.Select(p => p.Code).ToListAsync()).Should().Equal("reports:read");
        (await CodesHeldByRoleAsync(db, both)).Should().Equal("reports:read");
        (await CodesHeldByRoleAsync(db, oldOnly)).Should().Equal("reports:read");
        var overrides = await db.UserPermissionOverrides.ToListAsync();
        overrides.Should().HaveCount(2);
        overrides.Should().OnlyContain(o => o.PermissionId == target.Id);
        overrides.Should().ContainSingle(o => o.Id == moved.Id);
        overrides.Should().ContainSingle(o => o.UserId == sharedUser).Which.GrantType.Should().Be("revoke",
            "an override already on the new code is the newer decision and wins");
    }

    [Fact]
    public async Task Rename_FollowsAChainToTheLastCode_AndRenamesRoleTemplateCodes()
    {
        await using var db = NewDb();
        var old = AddPermission(db, "a:one");
        var role = AddRole(db, TenantA, "R");
        Grant(db, role, old);
        db.RoleTemplates.Add(new RoleTemplate
        {
            Id = Guid.NewGuid(), Name = "T", PermissionCodesJson = """["a:one","b:other"]""",
        });
        await db.SaveChangesAsync();

        await SyncAsync(db, Renames(("a:one", "a:two"), ("a:two", "a:three")), "a:three", "b:other");

        (await CodesHeldByRoleAsync(db, role)).Should().Equal("a:three");
        (await db.RoleTemplates.SingleAsync()).PermissionCodesJson.Should().Be("""["a:three","b:other"]""");
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        await using var db = NewDb();
        var old = AddPermission(db, "reports:view");
        Grant(db, AddRole(db, TenantA, "R"), old);
        await db.SaveChangesAsync();
        var rules = Renames(("reports:view", "reports:read"));

        await SyncAsync(db, rules, "reports:read", "reports:export");
        var second = await SyncAsync(db, rules, "reports:read", "reports:export");

        second.HasChanges.Should().BeFalse();
        (await db.PermissionChanges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DefaultGrant_BackfillsTheOwnerRoleOfExistingTenants_Once()
    {
        await using var db = NewDb();
        var ownerA = AddRole(db, TenantA, "Owner", isSystem: true);
        var ownerB = AddRole(db, TenantB, "Owner", isSystem: true);
        var employee = AddRole(db, TenantA, "Employee", isSystem: true);
        var customOwnerName = AddRole(db, TenantA, "Owner", isSystem: false);
        await db.SaveChangesAsync();
        var rules = new PermissionLifecycleRules(
            [],
            [new PermissionDefaultGrant("reports:export", [DefaultGrantTarget.Owner], Day, "new feature")],
            []);

        var report = await SyncAsync(db, rules, "reports:export");

        report.Added.Should().Be(1);
        report.DefaultGrantsAdded.Should().Be(2);
        (await CodesHeldByRoleAsync(db, ownerA)).Should().Equal("reports:export");
        (await CodesHeldByRoleAsync(db, ownerB)).Should().Equal("reports:export");
        (await CodesHeldByRoleAsync(db, employee)).Should().BeEmpty();
        (await CodesHeldByRoleAsync(db, customOwnerName)).Should().BeEmpty("only the built-in Owner role is targeted");
        var ledger = await db.PermissionChanges.SingleAsync();
        ledger.ChangeKey.Should().Be("default-grant:reports:export");
        ledger.Detail.Should().Contain(TenantA.ToString()).And.Contain(ownerB.Id.ToString());

        // An admin takes it away again; the next start must not overrule that.
        db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(rp => rp.RoleId == ownerA.Id));
        await db.SaveChangesAsync();
        var second = await SyncAsync(db, rules, "reports:export");

        second.DefaultGrantsAdded.Should().Be(0);
        (await CodesHeldByRoleAsync(db, ownerA)).Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultGrant_CanTargetHoldersOfAnotherCode_AndRolesFromATemplate()
    {
        await using var db = NewDb();
        var writers = AddPermission(db, "employees:write");
        var writer = AddRole(db, TenantA, "Writer");
        var reader = AddRole(db, TenantA, "Reader");
        Grant(db, writer, writers);
        var templateId = Guid.NewGuid();
        var fromTemplate = AddRole(db, TenantB, "HR", templateId: templateId);
        await db.SaveChangesAsync();
        var rules = new PermissionLifecycleRules(
            [],
            [new PermissionDefaultGrant(
                "checklists:manage",
                [DefaultGrantTarget.HoldersOfCode("employees:write"), DefaultGrantTarget.FromTemplate(templateId)],
                Day,
                "split out of employees:write")],
            []);

        await SyncAsync(db, rules, "employees:write", "checklists:manage");

        (await CodesHeldByRoleAsync(db, writer)).Should().BeEquivalentTo("employees:write", "checklists:manage");
        (await CodesHeldByRoleAsync(db, reader)).Should().BeEmpty();
        (await CodesHeldByRoleAsync(db, fromTemplate)).Should().Equal("checklists:manage");
    }

    [Theory]
    [InlineData(RetirementStage.SoftRetired, "soft-retire")]
    [InlineData(RetirementStage.Removed, "remove")]
    public async Task Retire_StillGranted_IsRefusedWithTheGrantCount(RetirementStage stage, string verb)
    {
        await using var db = NewDb();
        var code = AddPermission(db, "chat:read");
        Grant(db, AddRole(db, TenantA, "R1"), code);
        Grant(db, AddRole(db, TenantB, "R2"), code);
        Override(db, TenantB, code);
        await db.SaveChangesAsync();
        var rules = new PermissionLifecycleRules([], [], [new PermissionRetirement("chat:read", stage, "unused")]);

        var act = () => SyncAsync(db, rules);

        (await act.Should().ThrowAsync<PermissionRetirementRefusedException>()).Which.Message.Should().Be(
            $"Refusing to {verb} permission 'chat:read': 2 role grant(s) and 1 user override(s) in 2 tenant(s) " +
            "would be lost. Declare a rename to keep them under another code, or set DropGrants on the retirement " +
            "if losing this access is intended.");
        var row = await db.Permissions.IgnoreQueryFilters().SingleAsync();
        row.RetiredAt.Should().BeNull();
        (await db.RolePermissions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Retire_NotGranted_RemovesTheRow()
    {
        await using var db = NewDb();
        AddPermission(db, "chat:read");
        await db.SaveChangesAsync();
        var rules = new PermissionLifecycleRules(
            [], [], [new PermissionRetirement("chat:read", RetirementStage.Removed, "unused")]);

        var report = await SyncAsync(db, rules);

        report.Removed.Should().Be(1);
        (await db.Permissions.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.PermissionChanges.SingleAsync()).ChangeKey.Should().Be("remove:chat:read");
    }

    [Fact]
    public async Task SoftRetire_HidesTheCode_KeepsGrants_AndBringingItBackRestoresThem()
    {
        await using var db = NewDb();
        var code = AddPermission(db, "chat:read");
        var role = AddRole(db, TenantA, "R");
        Grant(db, role, code);
        await db.SaveChangesAsync();
        var retire = new PermissionLifecycleRules(
            [], [], [new PermissionRetirement("chat:read", RetirementStage.SoftRetired, "unused", DropGrants: true)]);

        var report = await SyncAsync(db, retire, "chat:write");

        report.SoftRetired.Should().Be(1);
        (await db.Permissions.Select(p => p.Code).ToListAsync()).Should().Equal("chat:write");
        (await CodesHeldByRoleAsync(db, role)).Should().BeEmpty("the resolver joins through the filtered permissions set");
        (await db.RolePermissions.IgnoreQueryFilters().CountAsync(rp => rp.PermissionId == code.Id)).Should().Be(1);

        var restored = await SyncAsync(db, PermissionLifecycleRules.Empty, "chat:read", "chat:write");

        restored.Restored.Should().Be(1);
        (await CodesHeldByRoleAsync(db, role)).Should().Equal("chat:read");
    }

    [Fact]
    public async Task InvalidDeclarations_FailBeforeAnyChange()
    {
        await using var db = NewDb();

        var act = () => SyncAsync(db, Renames(("a:old", "a:missing")), "a:new");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not in the catalogue*");
        (await db.Permissions.AnyAsync()).Should().BeFalse();
    }
}
