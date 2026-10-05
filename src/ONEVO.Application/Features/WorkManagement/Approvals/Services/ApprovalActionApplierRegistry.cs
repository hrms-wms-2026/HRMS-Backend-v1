namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class ApprovalActionApplierRegistry : IApprovalActionApplierRegistry
{
    private readonly IReadOnlyDictionary<string, IApprovalActionApplier> _byActionType;

    public ApprovalActionApplierRegistry(IEnumerable<IApprovalActionApplier> appliers)
    {
        var map = new Dictionary<string, IApprovalActionApplier>();
        foreach (var applier in appliers)
        {
            if (!map.TryAdd(applier.ActionType, applier))
                throw new InvalidOperationException($"Two approval appliers are registered for '{applier.ActionType}'.");
        }
        _byActionType = map;
    }

    public IApprovalActionApplier? Find(string actionType) => _byActionType.GetValueOrDefault(actionType);
}
