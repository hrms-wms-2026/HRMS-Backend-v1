using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.WorkLocation.Commands.ConfirmWorkLocation;

public sealed record ConfirmWorkLocationCommand(
    string LocationType,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters) : IRequest<Result>;
