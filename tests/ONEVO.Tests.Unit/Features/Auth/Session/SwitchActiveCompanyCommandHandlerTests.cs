using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.ActiveCompany.Commands.SwitchActiveCompany;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Tests.Unit.Features.Auth;

public class SwitchActiveCompanyCommandHandlerTests
{
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private SwitchActiveCompanyCommandHandler CreateHandler() =>
        new(_sessions.Object, _employees.Object, _legalEntities.Object, _unitOfWork.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_CallerHasActiveEmployeeInLegalEntity_UpdatesSessionActiveEmployeeId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var targetEmployeeId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = userId, TenantId = tenantId };
        var legalEntity = new LegalEntity { Id = legalEntityId, TenantId = tenantId, IsActive = true };
        var targetEmployee = new Employee
            { Id = targetEmployeeId, TenantId = tenantId, UserId = userId, LegalEntityId = legalEntityId };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);
        _employees.Setup(e => e.GetByUserAndLegalEntityAsync(tenantId, userId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetEmployee);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetEmployeeId, session.ActiveEmployeeId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerHasNoActiveEmployeeInLegalEntity_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = callerId, TenantId = tenantId };
        var legalEntity = new LegalEntity { Id = legalEntityId, TenantId = tenantId, IsActive = true };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(callerId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);
        _employees.Setup(e => e.GetByUserAndLegalEntityAsync(tenantId, callerId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_TargetEmployeeDoesNotExist_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = userId, TenantId = tenantId };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new SwitchActiveCompanyCommand(legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_InactiveLegalEntity_ReturnsConflictWithoutChangingSession()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var session = new ONEVO.Domain.Features.Auth.Entities.Session { Id = sessionId, UserId = userId, TenantId = tenantId };
        var legalEntity = new LegalEntity { Id = legalEntityId, TenantId = tenantId, IsActive = false };

        _currentUser.Setup(c => c.TenantId).Returns(tenantId);
        _currentUser.Setup(c => c.UserId).Returns(userId);
        _currentUser.Setup(c => c.SessionId).Returns(sessionId);
        _sessions.Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legalEntity);

        var result = await CreateHandler().Handle(new SwitchActiveCompanyCommand(legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Null(session.ActiveEmployeeId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
