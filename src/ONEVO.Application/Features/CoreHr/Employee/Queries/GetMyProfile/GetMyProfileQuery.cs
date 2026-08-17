using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;

public record GetMyProfileQuery : IRequest<Result<MyProfileResponse>>;
