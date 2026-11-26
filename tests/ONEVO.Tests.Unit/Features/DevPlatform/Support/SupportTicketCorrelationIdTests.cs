using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Helpers;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Application.Features.DevPlatform.Support.Tenant;
using ONEVO.Application.Features.DevPlatform.Support.Validation;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

/// <summary>DEC-114: a tenant ticket carries the references (correlation ids) of the user's recent failed requests.</summary>
public sealed class SupportTicketCorrelationIdTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static CreateTenantSupportTicketCommand Command(IReadOnlyList<string?>? ids) =>
        new("Payroll export", "Export is empty", "medium", null, ids);

    // ---------- validator ----------

    [Fact]
    public void Validator_NoIds_Passes()
    {
        var validator = new CreateTenantSupportTicketCommandValidator();
        validator.TestValidate(Command(null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(Command([])).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_FiveIdsAtMaxLength_Passes()
    {
        var ids = Enumerable.Range(0, 5).Select(i => new string((char)('a' + i), 100)).ToList<string?>();

        new CreateTenantSupportTicketCommandValidator().TestValidate(Command(ids)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_GuidAndHyphenatedIds_Pass()
        => new CreateTenantSupportTicketCommandValidator()
            .TestValidate(Command([Guid.NewGuid().ToString(), "0HN7ABC-00000001", Guid.NewGuid().ToString("N")]))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validator_SixIds_Fails()
    {
        var ids = Enumerable.Range(0, 6).Select(i => $"id-{i}").ToList<string?>();

        new CreateTenantSupportTicketCommandValidator().TestValidate(Command(ids))
            .ShouldHaveValidationErrorFor(x => x.CorrelationIds);
    }

    [Fact]
    public void Validator_IdLongerThan100_Fails()
        => new CreateTenantSupportTicketCommandValidator()
            .TestValidate(Command([new string('a', 101)]))
            .ShouldHaveValidationErrorFor("CorrelationIds[0]");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc def")]
    [InlineData("abc,def")]
    [InlineData("abc_def")]
    [InlineData("abc.def")]
    [InlineData("<script>")]
    [InlineData("abc\ndef")]
    [InlineData("abc\n")]
    [InlineData("äbc")]
    [InlineData(null)]
    public void Validator_DisallowedCharactersOrEmpty_Fail(string? id)
        => new CreateTenantSupportTicketCommandValidator()
            .TestValidate(Command(["good-id", id]))
            .ShouldHaveValidationErrorFor("CorrelationIds[1]");

    // ---------- rules (handler-side guard and storage format) ----------

    [Fact]
    public void Rules_JoinAndSplit_RoundTrip_WithoutDuplicates()
    {
        var stored = SupportTicketRules.JoinCorrelationIds(["a-1", "b-2", "a-1"]);

        stored.Should().Be("a-1,b-2");
        SupportTicketRules.SplitCorrelationIds(stored).Should().Equal("a-1", "b-2");
        SupportTicketRules.JoinCorrelationIds([]).Should().BeNull();
        SupportTicketRules.SplitCorrelationIds(null).Should().BeEmpty();
    }

    [Fact]
    public void Rules_ValidateCorrelationIds_RejectsWhatTheValidatorRejects()
    {
        SupportTicketRules.ValidateCorrelationIds(null).Should().BeNull();
        SupportTicketRules.ValidateCorrelationIds(["ok-1"]).Should().BeNull();
        SupportTicketRules.ValidateCorrelationIds(["a", "b", "c", "d", "e", "f"]).Should().NotBeNull();
        SupportTicketRules.ValidateCorrelationIds(["bad id"]).Should().NotBeNull();
    }

    // ---------- handlers ----------

    private readonly Mock<ISupportTicketRepository> _repo = new();
    private readonly Mock<ISupportNotifier> _notifier = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public SupportTicketCorrelationIdTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
        _currentUser.SetupGet(c => c.Email).Returns("owner@acme.test");
    }

    private CreateTenantSupportTicketCommandHandler CreateHandler() =>
        new(_repo.Object, _notifier.Object, _currentUser.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Create_StoresIdsCommaSeparated_AndDetailReturnsThemAsList()
    {
        SupportTicket? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicket, CancellationToken>((t, _) => added = t);
        var first = Guid.NewGuid().ToString();

        var result = await CreateHandler().Handle(Command([first, "0HN7ABC-00000001"]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.CorrelationIds.Should().Be($"{first},0HN7ABC-00000001");

        _repo.Setup(r => r.GetWithChildrenAsync(added.Id, TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(added);
        var detail = await new GetTenantSupportTicketQueryHandler(_repo.Object, _currentUser.Object)
            .Handle(new GetTenantSupportTicketQuery(added.Id), CancellationToken.None);

        detail.Value!.CorrelationIds.Should().Equal(first, "0HN7ABC-00000001");
        SupportMapper.ToDetailDto(added, "Acme", null).CorrelationIds.Should().Equal(first, "0HN7ABC-00000001");
    }

    [Fact]
    public async Task Create_WithoutIds_StoresNull_AndDetailReturnsEmptyList()
    {
        SupportTicket? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()))
            .Callback<SupportTicket, CancellationToken>((t, _) => added = t);

        (await CreateHandler().Handle(Command(null), CancellationToken.None)).IsSuccess.Should().BeTrue();

        added!.CorrelationIds.Should().BeNull();
        SupportMapper.ToTenantDetailDto(added).CorrelationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithInvalidIds_FailsWithoutSaving()
    {
        var result = await CreateHandler().Handle(Command(["has space"]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _repo.Verify(r => r.AddAsync(It.IsAny<SupportTicket>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
