namespace ONEVO.Application.Features.Leave.BalanceAudit.DTOs.Responses;

public record LeaveExportFile(byte[] Content, string ContentType, string FileName);
