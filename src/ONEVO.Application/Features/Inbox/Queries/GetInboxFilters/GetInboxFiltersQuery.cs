using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxFilters;

public sealed record GetInboxFiltersQuery : IRequest<Result<IReadOnlyList<InboxFilterField>>>;
