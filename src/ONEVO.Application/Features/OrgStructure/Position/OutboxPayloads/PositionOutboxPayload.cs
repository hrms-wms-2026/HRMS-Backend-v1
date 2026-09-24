namespace ONEVO.Application.Features.OrgStructure.OutboxPayloads;

// Shared by PositionCreated/Updated/Archived/Restored - all four carry the same identity shape.
// A concrete record (not an anonymous type) so IOutboxWriter.EnqueueAsync<TPayload> is a closed
// generic instantiation that unit tests can Setup/Verify against. Not named "...Events..." -
// LayerDependencyTests.ApplicationFeatures_ShouldNotUse_FeatureLevelEventsFolder forbids that
// namespace/folder convention under Features.*.
public sealed record PositionOutboxPayload(Guid PositionId, Guid LegalEntityId, Guid TenantId);
