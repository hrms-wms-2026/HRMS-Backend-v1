using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxCounts;

/// <summary>The rail badge: pending requests addressed to the caller, unread chat messages, open alerts.</summary>
public sealed record GetInboxCountsQuery : IRequest<Result<InboxCounts>>;
