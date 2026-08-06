# Invite Platform Manager (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a platform admin invite a new platform manager (email + full name + at least one existing role) and let the invited person accept and activate their account.

**Architecture:** `InvitePlatformManagerCommand` creates a `PlatformUser` row (`Status = Pending`) plus `PlatformUserRole` rows immediately, and a `PlatformUserInvite` row carrying only the hashed token. `AcceptPlatformManagerInviteCommand` (public, unauthenticated) resolves the invite by token hash, creates the `PlatformUserCredential`, and flips the user to `Active`. Email delivery goes through the existing outbox exclusively.

**Tech Stack:** .NET 10, MediatR, EF Core + Npgsql, xUnit + FluentAssertions + Moq.

## Global Constraints

- No zero-permission invites: `RoleIds` must be non-empty (spec, Backend changes §1).
- No new join table: roles go straight into the existing `platform_user_roles` table because the `PlatformUser` row exists from invite time onward (spec, Data model change).
- Token generation/hashing uses `ISecureTokenGenerator` (`GenerateOpaqueToken()` / `HashToken()`), matching `RequestAdminPasswordResetCommandHandler`/`ResetAdminPasswordCommandHandler` — not a hand-rolled SHA-256 helper.
- All email delivery goes through `IOutboxWriter` — never a direct `IEmailService` call from a command handler (architecture-test-enforced elsewhere in this codebase; this plan's Task 6 self-review must confirm neither new handler violates it).
- `/admin/v1/auth/accept-invite` is unauthenticated (`[AllowAnonymous]`), must be added to `CsrfProtectionMiddleware.ExemptPaths`, and needs its own `AuthRateLimitingMiddleware` rule (spec, Endpoint + CSRF note + Rate limiting note).
- No auto-login after accept — the frontend redirects to `/auth/login` (spec, step 8).

---

### Task 1: Data model — `PlatformUserId` on `PlatformUserInvite`

**Files:**
- Modify: `src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserInvite.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/PlatformAccess/PlatformUserInviteConfiguration.cs`
- Modify: `src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformAuthEvent.cs`
- Create: EF migration (via `dotnet ef migrations add`)

**Interfaces:**
- Produces: `PlatformUserInvite.PlatformUserId` (`Guid?`), `PlatformAuthEvent.PlatformManagerInvited` /
  `PlatformAuthEvent.PlatformManagerInviteAccepted` (string constants) — consumed by Tasks 3 and 6.

- [ ] **Step 1: Add the column to the entity**

In `PlatformUserInvite.cs`, add a property after `InviteTokenHash`:

```csharp
    public Guid? PlatformUserId { get; set; }
```

- [ ] **Step 2: Configure the FK**

In `PlatformUserInviteConfiguration.cs`, add after the existing `HasOne<PlatformUser>()` block
(which maps `InvitedById`):

```csharp
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(i => i.PlatformUserId)
            .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3: Add the two new auth-event constants**

In `PlatformAuthEvent.cs`, add after `AdminPasswordResetCompleted`:

```csharp
    public const string PlatformManagerInvited = "platform_manager_invited";
    public const string PlatformManagerInviteAccepted = "platform_manager_invite_accepted";
```

- [ ] **Step 4: Generate the migration**

Run (from repo root):
```bash
dotnet ef migrations add AddPlatformUserIdToPlatformUserInvite --project src/ONEVO.Infrastructure --startup-project src/ONEVO.Api
```
Expected: two new files under `src/ONEVO.Infrastructure/Migrations/` (a `*.cs` and a
`*.Designer.cs`), and `ApplicationDbContextModelSnapshot.cs` updated with the new
column + FK.

- [ ] **Step 5: Verify the build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformUserInvite.cs src/ONEVO.Domain/Features/DevPlatform/PlatformAccess/Entities/PlatformAuthEvent.cs src/ONEVO.Infrastructure/Persistence/Configurations/DevPlatform/PlatformAccess/PlatformUserInviteConfiguration.cs src/ONEVO.Infrastructure/Migrations/ src/ONEVO.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
git commit -m "feat: add PlatformUserId to PlatformUserInvite"
```

---

### Task 2: `IPlatformUserInviteRepository`

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/RepositoryInterfaces/IPlatformUserInviteRepository.cs`
- Modify: `src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/PlatformAccess/EfPlatformAccessRepository.cs`
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `PlatformUserInvite` (Task 1).
- Produces: `IPlatformUserInviteRepository` with `AddAsync`, `GetByTokenHashAsync`,
  `GetByPlatformUserIdAsync`, `Update` — consumed by Tasks 3, 5, and 6.
  `GetByPlatformUserIdAsync` (not `GetByIdAsync`) is deliberate: the frontend's users
  list only ever has `PlatformUser.Id` for a pending row (the list endpoint, Task 7,
  never exposes `PlatformUserInvite.Id` at all), so revoke must be keyed the same way
  everything else in this list already is — by the user's own ID, not the invite's.

- [ ] **Step 1: Write the interface**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/RepositoryInterfaces/IPlatformUserInviteRepository.cs
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserInviteRepository
{
    Task AddAsync(PlatformUserInvite invite, CancellationToken ct = default);
    Task<PlatformUserInvite?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<PlatformUserInvite?> GetByPlatformUserIdAsync(Guid platformUserId, CancellationToken ct = default);
    void Update(PlatformUserInvite invite);
}
```

- [ ] **Step 2: Implement it on `EfPlatformAccessRepository`**

Add `IPlatformUserInviteRepository` to the class's interface list (the `sealed class
EfPlatformAccessRepository :` line), and add this region at the end of the class,
before the closing brace:

```csharp
    // IPlatformUserInviteRepository

    public Task AddAsync(PlatformUserInvite invite, CancellationToken ct = default) =>
        _db.PlatformUserInvites.AddAsync(invite, ct).AsTask();

    public Task<PlatformUserInvite?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PlatformUserInvites.FirstOrDefaultAsync(i => i.InviteTokenHash == tokenHash, ct);

    public Task<PlatformUserInvite?> GetByPlatformUserIdAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUserInvites.FirstOrDefaultAsync(i => i.PlatformUserId == platformUserId, ct);

    public void Update(PlatformUserInvite invite) =>
        _db.PlatformUserInvites.Update(invite);
```

- [ ] **Step 3: Register the DI binding**

In `src/ONEVO.Infrastructure/DependencyInjection.cs`, add after the existing
`services.AddScoped<IPlatformAuthEventRepository>(...)` line:

```csharp
        services.AddScoped<IPlatformUserInviteRepository>(sp => sp.GetRequiredService<EfPlatformAccessRepository>());
```

- [ ] **Step 4: Verify the build**

Run: `dotnet build src/ONEVO.Infrastructure/ONEVO.Infrastructure.csproj`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/PlatformAccess/RepositoryInterfaces/IPlatformUserInviteRepository.cs src/ONEVO.Infrastructure/Persistence/Repositories/DevPlatform/PlatformAccess/EfPlatformAccessRepository.cs src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat: add IPlatformUserInviteRepository"
```

---

### Task 3: `InvitePlatformManagerCommand`

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/InvitePlatformManager/InvitePlatformManagerCommand.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/InvitePlatformManager/InvitePlatformManagerCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/InvitePlatformManagerCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IPlatformUserRepository` (`GetByEmailAsync`, `AddAsync`), `IPlatformRoleRepository`
  (`GetRoleByIdAsync`), `IPlatformUserInviteRepository.AddAsync` (Task 2),
  `ISecureTokenGenerator` (`GenerateOpaqueToken`, `HashToken`), `IOutboxWriter.EnqueueAsync`,
  `IUnitOfWork.SaveChangesAsync`, `PlatformUser.StatusPending`/`InvitePending` (already
  exist), `PlatformAuthEvent.PlatformManagerInvited` (Task 1).
- Produces: `InvitePlatformManagerCommand(string Email, string FullName, IReadOnlyList<Guid> RoleIds)`
  → `IRequestHandler<InvitePlatformManagerCommand, Result>` — consumed by Task 5's
  controller endpoint. `OutboxMessageTypes.PlatformManagerInviteEmail` and
  `PlatformManagerInviteEmailPayload` are declared here and consumed by Task 4's
  outbox handler.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/InvitePlatformManagerCommandHandlerTests.cs
using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class InvitePlatformManagerCommandHandlerTests
{
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformRoleRepository> _roles = new();
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<ICurrentPlatformUserContext> _currentUser = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IOutboxWriter> _outbox = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private InvitePlatformManagerCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        return new InvitePlatformManagerCommandHandler(
            _users.Object, _roles.Object, _invites.Object, _authEvents.Object, _currentUser.Object,
            _tokens.Object, _outbox.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_EmptyRoleIds_ReturnsFailure()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", []),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _users.Verify(u => u.AddAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownRoleId_ReturnsNotFound()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformRole?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ExistingEmail_ReturnsConflict()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUser { Id = Guid.NewGuid(), Email = "existing@example.com" });
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole { Id = roleId, Name = "Manager" });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("existing@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesUserRolesInviteAndOutboxMessage()
    {
        var roleId = Guid.NewGuid();
        _users.Setup(u => u.GetByEmailAsync("new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);
        _roles.Setup(r => r.GetRoleByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformRole { Id = roleId, Name = "Manager" });
        _tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-token-value");
        _tokens.Setup(t => t.HashToken("raw-token-value")).Returns("hashed-token-value");

        PlatformUser? capturedUser = null;
        _users.Setup(u => u.AddAsync(It.IsAny<PlatformUser>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformUser, CancellationToken>((u, _) => capturedUser = u)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(
            new InvitePlatformManagerCommand("new@example.com", "New Manager", [roleId]),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedUser.Should().NotBeNull();
        capturedUser!.Status.Should().Be(PlatformUser.StatusPending);
        capturedUser.Email.Should().Be("new@example.com");

        _invites.Verify(i => i.AddAsync(
            It.Is<PlatformUserInvite>(inv =>
                inv.InviteTokenHash == "hashed-token-value" &&
                inv.Email == "new@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);

        _outbox.Verify(o => o.EnqueueAsync(
            OutboxMessageTypes.PlatformManagerInviteEmail,
            It.Is<PlatformManagerInviteEmailPayload>(p =>
                p.Email == "new@example.com" && p.RawToken == "raw-token-value"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~InvitePlatformManagerCommandHandlerTests"`
Expected: FAIL to compile — `InvitePlatformManagerCommand`/`Handler` don't exist yet.

- [ ] **Step 3: Write the command**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/InvitePlatformManager/InvitePlatformManagerCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

public record InvitePlatformManagerCommand(
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds) : IRequest<Result>;
```

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/InvitePlatformManager/InvitePlatformManagerCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

/// <summary>
/// Creates the PlatformUser row (Pending) and its roles immediately, rather than at
/// acceptance time - see docs/superpowers/specs/2026-08-06-invite-platform-manager-design.md
/// "Data model change" for why this replaces an earlier join-table design.
/// </summary>
public sealed class InvitePlatformManagerCommandHandler : IRequestHandler<InvitePlatformManagerCommand, Result>
{
    private static readonly TimeSpan InviteValidity = TimeSpan.FromHours(72);

    private readonly IPlatformUserRepository _users;
    private readonly IPlatformRoleRepository _roles;
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public InvitePlatformManagerCommandHandler(
        IPlatformUserRepository users,
        IPlatformRoleRepository roles,
        IPlatformUserInviteRepository invites,
        IPlatformAuthEventRepository authEvents,
        ICurrentPlatformUserContext currentUser,
        ISecureTokenGenerator tokens,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _users = users;
        _roles = roles;
        _invites = invites;
        _authEvents = authEvents;
        _currentUser = currentUser;
        _tokens = tokens;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(InvitePlatformManagerCommand request, CancellationToken ct)
    {
        if (request.RoleIds.Count == 0)
            return Result.Failure("At least one role is required.", 400);

        foreach (var roleId in request.RoleIds)
        {
            var role = await _roles.GetRoleByIdAsync(roleId, ct);
            if (role is null)
                return Result.NotFound($"Role '{roleId}' not found.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null)
            return Result.Conflict($"A platform user with email '{email}' already exists or has a pending invite.");

        var now = _clock.UtcNow;
        var fullName = request.FullName.Trim();

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            Status = PlatformUser.StatusPending,
            InviteStatus = PlatformUser.InvitePending,
            CreatedAt = now
        };
        await _users.AddAsync(user, ct);

        // ReplaceRolesAsync removes any existing roles for the user then adds the
        // given set; on this brand-new user there's nothing to remove, so it's
        // equivalent to a plain bulk-add without needing a separate repository method.
        await _users.ReplaceRolesAsync(user.Id, request.RoleIds, ct);

        var invitedById = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "InvitePlatformManagerCommand requires an authenticated platform user.");

        var rawToken = _tokens.GenerateOpaqueToken();
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            InviteTokenHash = _tokens.HashToken(rawToken),
            InvitedById = invitedById,
            PlatformUserId = user.Id,
            ExpiresAt = now.Add(InviteValidity),
            CreatedAt = now
        };
        await _invites.AddAsync(invite, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.PlatformManagerInviteEmail,
            new PlatformManagerInviteEmailPayload(email, fullName, rawToken),
            tenantId: null,
            ct);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EventType = PlatformAuthEvent.PlatformManagerInvited,
            MetadataJson = "{}",
            CreatedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~InvitePlatformManagerCommandHandlerTests"`
Expected: PASS, 4/4.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/InvitePlatformManager/ tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/InvitePlatformManagerCommandHandlerTests.cs
git commit -m "feat: add InvitePlatformManagerCommand"
```

---

### Task 4: Invite email (outbox type, handler, template)

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/OutboxHandlers/PlatformManagerInviteEmailOutboxHandler.cs`
- Modify: `src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs`
- Modify: `src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs`
- Modify: `src/ONEVO.Application/DependencyInjection.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs`

**Interfaces:**
- Consumes: `OutboxMessageTypes.PlatformManagerInviteEmail`,
  `PlatformManagerInviteEmailPayload` (both declared in Task 3's
  `InvitePlatformManagerCommandHandler.cs` file — move `PlatformManagerInviteEmailPayload`'s
  declaration here instead if you prefer payload types living next to their handler;
  either location works as long as both files agree — this plan assumes it stays
  where Task 3 put it and this handler references it via `using`).
- Produces: `IEmailService.SendPlatformManagerInviteAsync(string to, string fullName,
  string inviteToken, CancellationToken ct)` — no other task depends on this.

- [ ] **Step 1: Write the failing template-renderer test**

Add to `tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs`
(same file Task 6 of the earlier CSRF/reset-link fix already touched — add a new
`[Fact]`, don't remove anything existing):

```csharp
    [Fact]
    public void RenderPlatformManagerInvite_UsesAdminConsoleBaseUrl()
    {
        var renderer = new EmailTemplateRenderer(Options.Create(new EmailOptions
        {
            AdminConsoleBaseUrl = "https://admin.localhost:4200"
        }));

        var rendered = renderer.Render("platform_manager_invite", new
        {
            full_name = "New Manager",
            invite_token = "tok-xyz"
        });

        rendered.HtmlBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
        rendered.TextBody.Should().Contain("https://admin.localhost:4200/auth/accept-invite?token=tok-xyz");
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RenderPlatformManagerInvite_UsesAdminConsoleBaseUrl"`
Expected: FAIL — `EmailTemplateRenderer.Render` throws `InvalidOperationException:
Unknown email template 'platform_manager_invite'`.

- [ ] **Step 3: Add the template case**

In `EmailTemplateRenderer.cs`, add `"platform_manager_invite" =>
RenderPlatformManagerInvite(fields),` to the `Render` method's `switch`, and add this
method next to `RenderAdminPasswordReset`:

```csharp
    private RenderedEmail RenderPlatformManagerInvite(IReadOnlyDictionary<string, object?> f)
    {
        var fullName = Get(f, "full_name");
        var token = Get(f, "invite_token");
        var inviteUrl = string.IsNullOrWhiteSpace(_options.AdminConsoleBaseUrl)
            ? $"[invite_url placeholder - set Email:AdminConsoleBaseUrl] token={token}"
            : $"{_options.AdminConsoleBaseUrl.TrimEnd('/')}/auth/accept-invite?token={token}";

        var subject = "You've been invited to ONEXSO Platform Administration";
        var html = $"""
            <!doctype html><html><body>
              <p>Hi {Escape(fullName)},</p>
              <p>You've been invited to join ONEXSO Platform Administration.</p>
              <p><a href="{Escape(inviteUrl)}">Accept invitation</a></p>
              <p>This link expires in 72 hours.</p>
            </body></html>
            """;
        var text = $"Hi {fullName},\n\nYou've been invited to join ONEXSO Platform Administration.\nAccept: {inviteUrl}\nThis link expires in 72 hours.";
        return new RenderedEmail(subject, html, text);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RenderPlatformManagerInvite_UsesAdminConsoleBaseUrl"`
Expected: PASS.

- [ ] **Step 5: Add the `IEmailService` method**

In `IEmailService.cs`, add after `SendAdminPasswordChangedAsync`:

```csharp
    Task SendPlatformManagerInviteAsync(string to, string fullName, string inviteToken, CancellationToken ct = default);
```

In `TransactionalEmailService.cs`, add after `SendAdminPasswordChangedAsync`:

```csharp
    public Task SendPlatformManagerInviteAsync(string to, string fullName, string inviteToken, CancellationToken ct = default)
        => SendTemplateAsync(to, "platform_manager_invite", new { full_name = fullName, invite_token = inviteToken }, ct);
```

- [ ] **Step 6: Write the outbox handler**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/OutboxHandlers/PlatformManagerInviteEmailOutboxHandler.cs
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;

public sealed class PlatformManagerInviteEmailOutboxHandler : IOutboxMessageHandler
{
    private readonly IEmailService _emailService;

    public PlatformManagerInviteEmailOutboxHandler(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public string Type => OutboxMessageTypes.PlatformManagerInviteEmail;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PlatformManagerInviteEmailPayload>(payloadJson)
            ?? throw new InvalidOperationException("platform_manager_invite_email payload is empty.");

        await _emailService.SendPlatformManagerInviteAsync(payload.Email, payload.FullName, payload.RawToken, ct);
    }
}
```

Also add the message-type constant and payload record — if Task 3 did not already add
them to `OutboxMessageTypes` in `IOutboxMessageHandler.cs`, add here:

```csharp
    public const string PlatformManagerInviteEmail = "platform_manager_invite_email";
```

and (if not already declared during Task 3) in a suitable payloads file:

```csharp
public sealed record PlatformManagerInviteEmailPayload(string Email, string FullName, string RawToken);
```

- [ ] **Step 7: Register the outbox handler**

In `src/ONEVO.Application/DependencyInjection.cs`, add after the existing
`services.AddScoped<IOutboxMessageHandler, AdminPasswordChangedEmailOutboxHandler>();`:

```csharp
        services.AddScoped<IOutboxMessageHandler, PlatformManagerInviteEmailOutboxHandler>();
```

- [ ] **Step 8: Verify the build**

Run: `dotnet build src/ONEVO.Application/ONEVO.Application.csproj`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/ONEVO.Application/Common/ServiceInterfaces/IEmailService.cs src/ONEVO.Infrastructure/ExternalServices/Email/TransactionalEmailService.cs src/ONEVO.Infrastructure/ExternalServices/Email/EmailTemplateRenderer.cs src/ONEVO.Application/Features/DevPlatform/PlatformAccess/OutboxHandlers/ src/ONEVO.Application/DependencyInjection.cs tests/ONEVO.Tests.Unit/Features/SharedPlatform/Email/EmailTemplateRendererTests.cs
git commit -m "feat: add platform manager invite email"
```

---

### Task 5: Invite + revoke endpoints

**Files:**
- Modify: `src/ONEVO.Api/Controllers/Admin/DevPlatform/PlatformAccess/PlatformAccessController.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Requests/InvitePlatformManagerRequest.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/RevokePlatformUserInvite/RevokePlatformUserInviteCommand.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/RevokePlatformUserInvite/RevokePlatformUserInviteCommandHandler.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/RevokePlatformUserInviteCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `InvitePlatformManagerCommand` (Task 3, with the `InvitedById` correction
  applied), `IPlatformUserInviteRepository`/`IPlatformUserRepository` (Task 2/existing).
- Produces: `POST /admin/v1/platform-access/users/invite`,
  `POST /admin/v1/platform-access/users/{platformUserId}/revoke-invite` — consumed by the
  frontend plan, no backend task depends on these.

- [ ] **Step 1: Write the failing revoke-command test**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/RevokePlatformUserInviteCommandHandlerTests.cs
using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class RevokePlatformUserInviteCommandHandlerTests
{
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private RevokePlatformUserInviteCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        return new RevokePlatformUserInviteCommandHandler(_invites.Object, _users.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUser?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokePlatformUserInviteCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ValidUser_RevokesInviteAndDeactivatesUser()
    {
        var userId = Guid.NewGuid();
        var invite = new PlatformUserInvite { Id = Guid.NewGuid(), PlatformUserId = userId };
        var user = new PlatformUser { Id = userId, Status = PlatformUser.StatusPending, InviteStatus = PlatformUser.InvitePending };

        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _invites.Setup(i => i.GetByPlatformUserIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(invite);

        var handler = CreateHandler();
        var result = await handler.Handle(new RevokePlatformUserInviteCommand(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invite.RevokedAt.Should().NotBeNull();
        user.Status.Should().Be(PlatformUser.StatusInactive);
        user.InviteStatus.Should().Be(PlatformUser.InviteRevoked);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RevokePlatformUserInviteCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Write the revoke command + handler**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/RevokePlatformUserInvite/RevokePlatformUserInviteCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public record RevokePlatformUserInviteCommand(Guid PlatformUserId) : IRequest<Result>;
```

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/RevokePlatformUserInvite/RevokePlatformUserInviteCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public sealed class RevokePlatformUserInviteCommandHandler : IRequestHandler<RevokePlatformUserInviteCommand, Result>
{
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformUserRepository _users;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RevokePlatformUserInviteCommandHandler(
        IPlatformUserInviteRepository invites,
        IPlatformUserRepository users,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _invites = invites;
        _users = users;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(RevokePlatformUserInviteCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.PlatformUserId, ct);
        if (user is null)
            return Result.NotFound($"Platform user '{request.PlatformUserId}' not found.");

        var invite = await _invites.GetByPlatformUserIdAsync(request.PlatformUserId, ct);
        if (invite is null)
            return Result.NotFound($"No invite found for platform user '{request.PlatformUserId}'.");

        var now = _clock.UtcNow;
        invite.RevokedAt = now;
        _invites.Update(invite);

        user.Status = PlatformUser.StatusInactive;
        user.InviteStatus = PlatformUser.InviteRevoked;
        _users.UpdateUser(user);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~RevokePlatformUserInviteCommandHandlerTests"`
Expected: PASS, 2/2.

- [ ] **Step 5: Add the request DTO**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Requests/InvitePlatformManagerRequest.cs
namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Requests;

public record InvitePlatformManagerRequest(string Email, string FullName, IReadOnlyList<Guid> RoleIds);
```

- [ ] **Step 6: Add the controller endpoints**

In `PlatformAccessController.cs`, add the necessary `using` for
`InvitePlatformManagerCommand`, `RevokePlatformUserInviteCommand`, and
`InvitePlatformManagerRequest`, then add after `UpdateUserRoles`:

```csharp
    [HttpPost("users/invite")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]
    public async Task<IActionResult> InviteManager([FromBody] InvitePlatformManagerRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new InvitePlatformManagerCommand(request.Email, request.FullName, request.RoleIds), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("users/{platformUserId}/revoke-invite")]
    [RequirePlatformPermission(PlatformPermissionCatalog.AccountsManage)]
    public async Task<IActionResult> RevokeInvite(Guid platformUserId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokePlatformUserInviteCommand(platformUserId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Verify the build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/ONEVO.Api/Controllers/Admin/DevPlatform/PlatformAccess/PlatformAccessController.cs src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Requests/InvitePlatformManagerRequest.cs src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/RevokePlatformUserInvite/ tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/RevokePlatformUserInviteCommandHandlerTests.cs
git commit -m "feat: add invite and revoke-invite endpoints"
```

---

### Task 6: `AcceptPlatformManagerInviteCommand` + endpoint + CSRF + rate limit

**Files:**
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/AcceptPlatformManagerInvite/AcceptPlatformManagerInviteCommand.cs`
- Create: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/AcceptPlatformManagerInvite/AcceptPlatformManagerInviteCommandHandler.cs`
- Create: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Auth/AcceptPlatformManagerInviteRequest.cs`
- Modify: `src/ONEVO.Api/Controllers/Admin/DevPlatform/Auth/AdminAuthController.cs`
- Modify: `src/ONEVO.Api/Middleware/CsrfProtectionMiddleware.cs`
- Modify: `src/ONEVO.Api/Middleware/AuthRateLimitingMiddleware.cs`
- Modify: `tests/ONEVO.Tests.Architecture/PasswordResetHardeningArchitectureTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/AcceptPlatformManagerInviteCommandHandlerTests.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/Auth/CsrfProtectionMiddlewareTests.cs`

**Interfaces:**
- Consumes: `IPlatformUserInviteRepository.GetByTokenHashAsync` (Task 2),
  `IPlatformUserCredentialRepository.AddAsync`, `IPasswordHasher.Hash`,
  `ISecureTokenGenerator.HashToken`.
- Produces: `POST /admin/v1/auth/accept-invite` — consumed only by the frontend plan.

- [ ] **Step 1: Write the failing command-handler tests**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/AcceptPlatformManagerInviteCommandHandlerTests.cs
using Moq;
using FluentAssertions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class AcceptPlatformManagerInviteCommandHandlerTests
{
    private readonly Mock<IPlatformUserInviteRepository> _invites = new();
    private readonly Mock<IPlatformUserRepository> _users = new();
    private readonly Mock<IPlatformUserCredentialRepository> _credentials = new();
    private readonly Mock<IPlatformAuthEventRepository> _authEvents = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ISecureTokenGenerator> _tokens = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly DateTimeOffset _now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    private AcceptPlatformManagerInviteCommandHandler CreateHandler()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _tokens.Setup(t => t.HashToken("raw-token")).Returns("hashed-token");
        return new AcceptPlatformManagerInviteCommandHandler(
            _invites.Object, _users.Object, _credentials.Object, _authEvents.Object,
            _passwordHasher.Object, _tokens.Object, _clock.Object, _uow.Object);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsNotFound()
    {
        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserInvite?)null);

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(true, false, false, "already been accepted")]
    [InlineData(false, true, false, "been revoked")]
    [InlineData(false, false, true, "has expired")]
    public async Task Handle_UnusableInvite_ReturnsSpecificMessage(
        bool accepted, bool revoked, bool expired, string expectedFragment)
    {
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            PlatformUserId = Guid.NewGuid(),
            AcceptedAt = accepted ? _now.AddMinutes(-5) : null,
            RevokedAt = revoked ? _now.AddMinutes(-5) : null,
            ExpiresAt = expired ? _now.AddMinutes(-5) : _now.AddHours(1)
        };
        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain(expectedFragment);
    }

    [Fact]
    public async Task Handle_ValidInvite_ActivatesUserAndCreatesCredential()
    {
        var userId = Guid.NewGuid();
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            PlatformUserId = userId,
            ExpiresAt = _now.AddHours(1)
        };
        var user = new PlatformUser { Id = userId, Status = PlatformUser.StatusPending, InviteStatus = PlatformUser.InvitePending };

        _invites.Setup(i => i.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        _users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasher.Setup(p => p.Hash("NewPassword1!")).Returns("hashed-password");

        var result = await CreateHandler().Handle(
            new AcceptPlatformManagerInviteCommand("raw-token", "NewPassword1!"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(PlatformUser.StatusActive);
        user.InviteStatus.Should().Be(PlatformUser.InviteAccepted);
        invite.AcceptedAt.Should().Be(_now);
        _credentials.Verify(c => c.AddAsync(
            It.Is<PlatformUserCredential>(cred =>
                cred.PlatformUserId == userId && cred.PasswordHash == "hashed-password"),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptPlatformManagerInviteCommandHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Write the command**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/AcceptPlatformManagerInvite/AcceptPlatformManagerInviteCommand.cs
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;

public record AcceptPlatformManagerInviteCommand(string RawToken, string Password) : IRequest<Result>;
```

- [ ] **Step 4: Write the handler**

```csharp
// src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/AcceptPlatformManagerInvite/AcceptPlatformManagerInviteCommandHandler.cs
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;

public sealed class AcceptPlatformManagerInviteCommandHandler : IRequestHandler<AcceptPlatformManagerInviteCommand, Result>
{
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformUserRepository _users;
    private readonly IPlatformUserCredentialRepository _credentials;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public AcceptPlatformManagerInviteCommandHandler(
        IPlatformUserInviteRepository invites,
        IPlatformUserRepository users,
        IPlatformUserCredentialRepository credentials,
        IPlatformAuthEventRepository authEvents,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _invites = invites;
        _users = users;
        _credentials = credentials;
        _authEvents = authEvents;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(AcceptPlatformManagerInviteCommand request, CancellationToken ct)
    {
        var tokenHash = _tokens.HashToken(request.RawToken);
        var invite = await _invites.GetByTokenHashAsync(tokenHash, ct);
        if (invite is null)
            return Result.NotFound("Invitation not found.");

        var now = _clock.UtcNow;
        if (invite.AcceptedAt is not null)
            return Result.Failure("This invitation has already been accepted.", 400);
        if (invite.RevokedAt is not null)
            return Result.Failure("This invitation has been revoked.", 400);
        if (invite.ExpiresAt <= now)
            return Result.Failure("This invitation has expired.", 400);

        var user = invite.PlatformUserId.HasValue
            ? await _users.GetByIdAsync(invite.PlatformUserId.Value, ct)
            : null;
        if (user is null)
            return Result.Failure("Invited user record not found.", 500);

        await _credentials.AddAsync(new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType,
            PasswordHash = _passwordHasher.Hash(request.Password),
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm,
            PasswordChangedAt = now,
            MustChangePassword = false,
            CreatedAt = now
        }, ct);

        user.Status = PlatformUser.StatusActive;
        user.InviteStatus = PlatformUser.InviteAccepted;
        _users.UpdateUser(user);

        invite.AcceptedAt = now;
        _invites.Update(invite);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EventType = PlatformAuthEvent.PlatformManagerInviteAccepted,
            MetadataJson = "{}",
            CreatedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptPlatformManagerInviteCommandHandlerTests"`
Expected: PASS, 5/5.

- [ ] **Step 6: Add the request DTO and controller endpoint**

```csharp
// src/ONEVO.Api/Controllers/Admin/DevPlatform/Auth/AcceptPlatformManagerInviteRequest.cs
namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public record AcceptPlatformManagerInviteRequest(string Token, string Password);
```

In `AdminAuthController.cs`, add the necessary `using` for
`AcceptPlatformManagerInviteCommand`, then add after `ResetPassword`:

```csharp
    /// <summary>Accepts a platform-manager invite: sets the account's password and
    /// activates it. Never returns which specific token state failed beyond the
    /// four distinct messages the command already returns (not found / accepted /
    /// revoked / expired) - those are safe to show, they don't leak account
    /// existence the way a password-reset error would.</summary>
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite(
        [FromBody] AcceptPlatformManagerInviteRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AcceptPlatformManagerInviteCommand(request.Token, request.Password), ct);
        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
```

- [ ] **Step 7: Add the CSRF exemption**

In `CsrfProtectionMiddleware.cs`, add `"/admin/v1/auth/accept-invite",` to
`ExemptPaths`, alongside `/admin/v1/auth/forgot-password` and
`/admin/v1/auth/reset-password`.

Add a test to `tests/ONEVO.Tests.Unit/Features/Auth/CsrfProtectionMiddlewareTests.cs`
mirroring whatever existing test proves `/admin/v1/auth/forgot-password` is exempt
even with a stale `admin_session` cookie present (find it in that file and copy its
shape with the path changed to `/admin/v1/auth/accept-invite`).

- [ ] **Step 8: Add the rate-limit rule**

In `AuthRateLimitingMiddleware.cs`, add to the `Rules` array, after the
`/admin/v1/auth/reset-password` rules:

```csharp
        new("/admin/v1/auth/accept-invite", "ip", null, 10, TimeSpan.FromMinutes(15)),
        new("/admin/v1/auth/accept-invite", "token", "token", 5, TimeSpan.FromMinutes(15)),
```

- [ ] **Step 9: Extend the architecture test**

In `tests/ONEVO.Tests.Architecture/PasswordResetHardeningArchitectureTests.cs`, in
`AuthRateLimitingMiddleware_StillCoversForgotResetAndForceChangePassword`, add
`"/admin/v1/auth/accept-invite"` to the path array being checked.

- [ ] **Step 10: Run the full affected test set**

Run:
```bash
dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~AcceptPlatformManagerInviteCommandHandlerTests|FullyQualifiedName~CsrfProtectionMiddlewareTests"
dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj --filter "FullyQualifiedName~AuthRateLimitingMiddleware_StillCoversForgotResetAndForceChangePassword"
```
Expected: all PASS.

- [ ] **Step 11: Verify the build**

Run: `dotnet build src/ONEVO.Api/ONEVO.Api.csproj`
Expected: 0 errors.

- [ ] **Step 12: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Commands/AcceptPlatformManagerInvite/ src/ONEVO.Api/Controllers/Admin/DevPlatform/Auth/ src/ONEVO.Api/Middleware/CsrfProtectionMiddleware.cs src/ONEVO.Api/Middleware/AuthRateLimitingMiddleware.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/PlatformAccess/AcceptPlatformManagerInviteCommandHandlerTests.cs tests/ONEVO.Tests.Unit/Features/Auth/CsrfProtectionMiddlewareTests.cs tests/ONEVO.Tests.Architecture/PasswordResetHardeningArchitectureTests.cs
git commit -m "feat: add accept-invite endpoint, CSRF exemption, and rate limit"
```

---

### Task 7: List endpoint — `Status` replaces `IsActive`

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Responses/PlatformUserResponse.cs`
- Modify: `src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Mappers/PlatformAccessMapper.cs`
- Test: existing tests referencing `PlatformUserResponse.IsActive`

**Interfaces:**
- Consumes: `PlatformUser.Status` (existing).
- Produces: `PlatformUserResponse.Status: string` — consumed by the frontend plan.

- [ ] **Step 1: Find every existing reference to `PlatformUserResponse`'s `IsActive`**

Run: `grep -rn "IsActive" src/ONEVO.Application/Features/DevPlatform/PlatformAccess tests/ONEVO.Tests.Unit/Features/DevPlatform`
Expected: shows `PlatformUserResponse.cs`, `PlatformAccessMapper.cs`'s two `Map(PlatformUser...)`
overloads, and any test asserting on `.IsActive`.

- [ ] **Step 2: Update the response records**

In `PlatformUserResponse.cs`, replace `bool IsActive` with `string Status`:

```csharp
public record PlatformUserResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
```

Apply the same `bool IsActive` → `string Status` change to
`PlatformUserDetailResponse` if that record also carries `IsActive` (check it — the
Task 3 read of `PlatformAccessMapper.cs` showed `MapDetail` using the same
`user.Status == PlatformUser.StatusActive` expression; decide whether the detail
response needs the same three-state treatment or can stay boolean, since pending
users may never reach the detail screen in this sub-project — if unsure, leave
`PlatformUserDetailResponse` as `bool IsActive` and only change the list response,
since only the list needs to distinguish "pending").

- [ ] **Step 3: Update the mapper**

In `PlatformAccessMapper.cs`, change the `Map(PlatformUser user, string role)`
overload's `IsActive` line to:

```csharp
            user.Status,
```

(passing the raw `Status` string straight through — `PlatformUser.Status` is already
`"pending"`/`"active"`/`"inactive"`, matching the three values the frontend spec
expects).

- [ ] **Step 4: Update any breaking tests**

Run: `dotnet build tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj` and fix each
compile error by changing `IsActive: true/false` construction sites to
`Status: PlatformUser.StatusActive` / `PlatformUser.StatusInactive` /
`PlatformUser.StatusPending` as appropriate to what the test is asserting.

- [ ] **Step 5: Run the full unit test suite for this area**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~PlatformAccess"`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Application/Features/DevPlatform/PlatformAccess/DTOs/Responses/PlatformUserResponse.cs src/ONEVO.Application/Features/DevPlatform/PlatformAccess/Mappers/PlatformAccessMapper.cs tests/
git commit -m "feat: replace PlatformUserResponse.IsActive with Status"
```

---

## Self-Review Notes

- **Spec coverage:** invite command (Task 3), email (Task 4), invite/revoke endpoints
  (Task 5), accept command + endpoint + CSRF + rate limit (Task 6), list-endpoint
  status change (Task 7), data model (Task 1), repository (Task 2) — every section of
  the spec's "Backend changes" maps to a task.
- **Type consistency:** `InvitePlatformManagerCommand`/`Handler`,
  `RevokePlatformUserInviteCommand`/`Handler`,
  `AcceptPlatformManagerInviteCommand`/`Handler` names and constructor parameter
  types match between their defining task and every later task that references them.
