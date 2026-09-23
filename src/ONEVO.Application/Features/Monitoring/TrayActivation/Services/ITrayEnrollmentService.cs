using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Services;

public interface ITrayEnrollmentService
{
    Task<TrayAuthResponseDto> IssueAsync(TrayEnrollmentRequest request, CancellationToken ct);
}
