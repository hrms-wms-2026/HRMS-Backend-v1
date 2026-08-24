namespace ONEVO.Application.Features.Monitoring.DailyReport.DTOs.Responses;

public sealed record DailyReportExportFile(byte[] Content, string ContentType, string FileName);
