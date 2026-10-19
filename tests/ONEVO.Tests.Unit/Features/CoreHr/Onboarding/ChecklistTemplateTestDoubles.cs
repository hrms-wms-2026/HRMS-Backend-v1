using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

internal static class ChecklistTemplateTestDoubles
{
    /// <summary>An asset repository holding no checklist template attachments.</summary>
    public static IEntityAssetRepository NoTemplateFiles()
    {
        var assets = new Mock<IEntityAssetRepository>();
        assets.Setup(a => a.ListByOwnerAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EntityAssetWithFile>());
        return assets.Object;
    }
}
