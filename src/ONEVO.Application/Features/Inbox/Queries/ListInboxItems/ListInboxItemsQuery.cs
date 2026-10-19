using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Queries.ListInboxItems;

/// <summary>Every item involving the caller across all source modules, filtered, sorted and paged.</summary>
public sealed record ListInboxItemsQuery(InboxQuery Query) : IRequest<Result<InboxPage>>;
