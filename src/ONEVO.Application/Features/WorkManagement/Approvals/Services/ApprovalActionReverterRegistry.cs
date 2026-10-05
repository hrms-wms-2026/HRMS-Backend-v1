namespace ONEVO.Application.Features.WorkManagement.Approvals.Services;

public sealed class ApprovalActionReverterRegistry : IApprovalActionReverterRegistry
{
    private readonly IReadOnlyDictionary<string, IApprovalActionReverter> _byActionType;

    public ApprovalActionReverterRegistry(IEnumerable<IApprovalActionReverter> reverters)
    {
        var map = new Dictionary<string, IApprovalActionReverter>();
        foreach (var reverter in reverters)
        {
            if (!map.TryAdd(reverter.ActionType, reverter))
                throw new InvalidOperationException($"Two approval reverters are registered for '{reverter.ActionType}'.");
        }
        _byActionType = map;
    }

    public IApprovalActionReverter? Find(string actionType) => _byActionType.GetValueOrDefault(actionType);
}
