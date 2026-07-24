using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Users.DTOs;

namespace ONEVO.Application.Features.Users.Queries.GetMyProfile;

public sealed record GetMyProfileQuery : IRequest<Result<MyProfileDto>>;
