namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.Queries.GetBulkOnboardingTemplate;

public sealed record GetBulkOnboardingTemplateQuery(string Format);

public sealed record BulkOnboardingTemplateFile(byte[] Content, string ContentType, string FileName);
