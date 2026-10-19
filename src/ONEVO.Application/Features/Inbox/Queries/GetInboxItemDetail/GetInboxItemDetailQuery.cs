using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Queries.GetInboxItemDetail;

public sealed record GetInboxItemDetailQuery(string Source, string Kind, Guid Id) : IRequest<Result<InboxItemDetail>>;
