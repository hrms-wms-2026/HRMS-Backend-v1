using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyAvatar;

public record GetMyAvatarQuery() : IRequest<Result<FileStreamDto>>;
