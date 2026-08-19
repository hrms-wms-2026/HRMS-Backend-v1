using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.OrgStructure.Mappers;
using Xunit;

using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class PositionMapperTests
{
    [Fact]
    public void ToListItemResponse_ReturnsZeroAssignedCountAndEmptyPreview_WhenPositionMissingFromDictionary()
    {
        var position = CreatePosition(maxOccupancy: 5);

        var response = PositionMapper.ToListItemResponse(
            position, new Dictionary<Guid, PositionOccupancyPreview>(), new Dictionary<Guid, bool>());

        Assert.Equal(0, response.AssignedCount);
        Assert.Equal(0, response.RemainingAssignedCount);
        Assert.Empty(response.OccupantPreview);
        Assert.Equal(5, response.MaxOccupancy);
    }

    [Fact]
    public void ToListItemResponse_ComputesDisplayNameAndInitials()
    {
        var position = CreatePosition(maxOccupancy: 1);
        var employeeId = Guid.NewGuid();
        var avatarFileId = Guid.NewGuid();
        var occupancy = new Dictionary<Guid, PositionOccupancyPreview>
        {
            [position.Id] = new PositionOccupancyPreview(
                1,
                [new PositionOccupantPreviewItem(employeeId, "Jane", "Smith", avatarFileId)])
        };

        var response = PositionMapper.ToListItemResponse(position, occupancy, new Dictionary<Guid, bool>());

        var occupant = Assert.Single(response.OccupantPreview);
        Assert.Equal("Jane Smith", occupant.DisplayName);
        Assert.Equal("JS", occupant.Initials);
        Assert.Equal(avatarFileId, occupant.AvatarFileId);
        Assert.Null(occupant.AvatarUrl);
    }

    [Fact]
    public void ToListItemResponse_ComputesRemainingAssignedCount_WhenPreviewIsTruncated()
    {
        var position = CreatePosition(maxOccupancy: 10);
        var previewItems = Enumerable.Range(0, 4)
            .Select(i => new PositionOccupantPreviewItem(Guid.NewGuid(), $"First{i}", $"Last{i}", null))
            .ToList();
        var occupancy = new Dictionary<Guid, PositionOccupancyPreview>
        {
            [position.Id] = new PositionOccupancyPreview(7, previewItems)
        };

        var response = PositionMapper.ToListItemResponse(position, occupancy, new Dictionary<Guid, bool>());

        Assert.Equal(7, response.AssignedCount);
        Assert.Equal(4, response.OccupantPreview.Count);
        Assert.Equal(3, response.RemainingAssignedCount);
    }

    [Fact]
    public void ToOccupantPreviewResponse_FallsBackToQuestionMark_WhenBothNamesAreBlank()
    {
        var item = new PositionOccupantPreviewItem(Guid.NewGuid(), " ", " ", null);

        var response = PositionMapper.ToOccupantPreviewResponse(item);

        Assert.Equal("?", response.Initials);
    }

    private static PositionEntity CreatePosition(int maxOccupancy)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LegalEntityId = Guid.NewGuid(),
            Name = "Engineer",
            Code = "ENG",
            MaxOccupancy = maxOccupancy,
            IsActive = true
        };
}
