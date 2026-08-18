using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.Commands.SaveOnboardingDraft;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using OnboardingDraftEntity = ONEVO.Domain.Features.CoreHr.Entities.OnboardingDraft;

namespace ONEVO.Tests.Unit.Features.CoreHr.OnboardingDrafts;

public sealed class OnboardingDraftWriteServiceTests
{
    private readonly Mock<IOnboardingDraftRepository> _draftRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IPositionRepository> _positionRepository = new();
    private readonly Mock<ILegalEntityRepository> _legalEntityRepository = new();
    private readonly Mock<IDepartmentRepository> _departmentRepository = new();
    private readonly Mock<ISeatEntitlementService> _seatEntitlementService = new();
    private readonly Mock<IWorkModeRepository> _workModeRepository = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public OnboardingDraftWriteServiceTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        _employeeRepository
            .Setup(r => r.EmployeeExistsInLegalEntityAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _employeeRepository
            .Setup(r => r.EmployeeNumberExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _seatEntitlementService
            .Setup(s => s.EvaluateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeatDecision(SeatDecisionStatus.Undetermined, null, 0, 0, null, false, true, "no source"));
        _workModeRepository.Setup(r => r.ExistsActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _legalEntityRepository.Setup(r => r.GetByIdForTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LegalEntity { IsActive = true });
        _positionRepository.Setup(r => r.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Position { IsActive = true });
        _draftRepository
            .Setup(r => r.GetResponseByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid id, CancellationToken _) =>
                new OnboardingDraftResponse(id, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
                    "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null,
                    OnboardingDraftStatus.Draft, OnboardingDraftReason.SeatConfigurationRequired,
                    OnboardingWizardStep.EmployeeDetails, Guid.Empty, "1"));
    }

    [Fact]
    public async Task SaveAsync_WithExplicitTenantAndUser_DoesNotReadICurrentUser()
    {
        var explicitTenantId = Guid.NewGuid();
        var explicitUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>(MockBehavior.Strict);

        OnboardingDraftEntity? added = null;
        _draftRepository.Setup(r => r.AddAsync(It.IsAny<OnboardingDraftEntity>(), It.IsAny<CancellationToken>()))
            .Callback<OnboardingDraftEntity, CancellationToken>((d, _) => added = d)
            .Returns(Task.CompletedTask);

        var service = new OnboardingDraftWriteService(
            _draftRepository.Object, _employeeRepository.Object,
            Mock.Of<IUserRepository>(), Mock.Of<IUserRoleRepository>(),
            _positionRepository.Object, Mock.Of<IPositionAssignmentRepository>(),
            _legalEntityRepository.Object, _departmentRepository.Object,
            Mock.Of<IEmploymentTypeRepository>(), _workModeRepository.Object,
            _seatEntitlementService.Object, Mock.Of<IAccessGrantRequestRepository>(),
            Mock.Of<IPermissionRepository>(), Mock.Of<IChecklistTemplateRepository>(),
            Mock.Of<IEmployeeChecklistTaskRepository>(), Mock.Of<IInvitationTokenRepository>(),
            Mock.Of<ITenantRepository>(), Mock.Of<IOutboxWriter>(),
            Mock.Of<ISecureTokenGenerator>(), currentUser.Object, _clock.Object);

        var command = new SaveOnboardingDraftCommand(
            null, "Ada", "Lovelace", "ada@test.dev", Guid.NewGuid(), null, null,
            "full_time", DateOnly.FromDateTime(DateTime.UtcNow), null, 1, null, null,
            OnboardingWizardStep.EmployeeDetails, null);

        var result = await service.SaveAsync(explicitTenantId, explicitUserId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(explicitTenantId, added!.TenantId);
        Assert.Equal(explicitUserId, added.StartedById);
        currentUser.VerifyGet(u => u.TenantId, Times.Never);
        currentUser.VerifyGet(u => u.UserId, Times.Never);
    }
}
