using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Conversations.Queries.SearchConversationPeople;

public sealed record ConversationPersonResponse(Guid EmployeeId, string Name, Guid? AvatarFileId);

/// <summary>Active colleagues to add to a conversation (or add and tag), by name.</summary>
public sealed record SearchConversationPeopleQuery(string? Search) : IRequest<Result<IReadOnlyList<ConversationPersonResponse>>>;
