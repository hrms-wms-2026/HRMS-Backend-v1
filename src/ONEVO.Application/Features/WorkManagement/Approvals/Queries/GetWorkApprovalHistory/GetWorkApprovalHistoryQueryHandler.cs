using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.DTOs;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.WorkManagement.Approvals.Queries.GetWorkApprovalHistory;

public sealed class GetWorkApprovalHistoryQueryHandler
    : IRequestHandler<GetWorkApprovalHistoryQuery, Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkApprovalHistoryRepository _history;

    public GetWorkApprovalHistoryQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IWorkApprovalHistoryRepository history)
    {
        _currentUser = currentUser;
        _identity = identity;
        _history = history;
    }

    public async Task<Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>> Handle(
        GetWorkApprovalHistoryQuery request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>.Forbidden(
                "No employee record for the current user.");

        var records = await _history.ListForEmployeeAsync(
            tenantId, request.ProjectId, callerEmployeeId.Value, ct);

        var employeeIds = records
            .SelectMany(record => new Guid?[]
            {
                record.RequestedById,
                record.ApproverId,
                record.DecidedById
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, employeeIds, ct);

        var items = records.Select(record => new WorkApprovalHistoryItemResponse(
            record.Id,
            record.ObjectiveId,
            record.Kind,
            record.Status,
            record.RequestedById == callerEmployeeId.Value ? "sent" : "received",
            record.SubjectTitle,
            BuildDetail(record.Kind, record.PayloadJson),
            record.RequestedById,
            names.GetValueOrDefault(record.RequestedById) ?? "Unknown employee",
            record.ApproverId,
            names.GetValueOrDefault(record.ApproverId) ?? "Unknown employee",
            record.DecidedById,
            record.DecidedById is Guid decidedById
                ? names.GetValueOrDefault(decidedById) ?? "Unknown employee"
                : null,
            record.DecisionComment,
            record.CreatedAt,
            record.DecidedAt)).ToList();

        return Result<IReadOnlyList<WorkApprovalHistoryItemResponse>>.Success(items);
    }

    private static string? BuildDetail(string kind, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;

        try
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var root = payload.RootElement;

            if (kind == "allocation_extend")
            {
                var hours = TryGetDecimal(root, "requestedAdditionalHours");
                var reason = TryGetString(root, "reason");
                if (hours is not null && !string.IsNullOrWhiteSpace(reason))
                    return $"{hours:0.##} additional hours - {reason}";
                if (hours is not null) return $"{hours:0.##} additional hours";
                return reason;
            }

            if (kind == "task_creation")
                return TryGetString(root, "title");

            if (kind is "task_edit" or "objective_edit")
            {
                var proposedTitle = TryGetString(root, "title");
                return string.IsNullOrWhiteSpace(proposedTitle)
                    ? null
                    : $"Proposed title: {proposedTitle}";
            }

            if (kind == "objective_invitation")
            {
                var inviteType = TryGetString(root, "inviteType");
                return string.IsNullOrWhiteSpace(inviteType)
                    ? null
                    : $"Invited as {inviteType}";
            }
        }
        catch (JsonException)
        {
            // Historical rows can predate the current payload contract. The history still remains
            // useful without a detail line, so malformed legacy JSON is intentionally ignored.
        }

        return null;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.TryGetDecimal(out var number)
            ? number
            : null;
}
