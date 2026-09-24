using FluentAssertions;
using Moq;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Infrastructure.Services.CoreHr.Onboarding;
using Xunit;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public class ChecklistTemplateAssigneeResolverTests
{
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentRepository = new();
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();

    private ChecklistTemplateAssigneeResolver CreateSut() => new(_positionAssignmentRepository.Object, _employeeRepository.Object);

    [Fact]
    public async Task ResolveAsync_SingleOccupant_ReturnsTheirUserId()
    {
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _positionAssignmentRepository
            .Setup(r => r.GetOccupancyPreviewsAsync(_tenantId, It.Is<IReadOnlyCollection<Guid>>(c => c.Single() == _positionId), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>
            {
                [_positionId] = new(1, new List<PositionOccupantPreviewItem> { new(employeeId, "Ada", "Lovelace", null) }),
            });
        _employeeRepository
            .Setup(r => r.GetByIdAsync(_tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = employeeId, UserId = userId });

        var result = await CreateSut().ResolveAsync(_tenantId, _positionId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(userId);
    }

    [Fact]
    public async Task ResolveAsync_ZeroOccupants_Fails()
    {
        _positionAssignmentRepository
            .Setup(r => r.GetOccupancyPreviewsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>());

        var result = await CreateSut().ResolveAsync(_tenantId, _positionId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task ResolveAsync_MultipleOccupants_Fails_RequiringManualAssignedToId()
    {
        _positionAssignmentRepository
            .Setup(r => r.GetOccupancyPreviewsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>
            {
                [_positionId] = new(2, new List<PositionOccupantPreviewItem> { new(Guid.NewGuid(), "A", "B", null) }),
            });

        var result = await CreateSut().ResolveAsync(_tenantId, _positionId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        result.Error.Should().Contain("multiple");
    }
}
