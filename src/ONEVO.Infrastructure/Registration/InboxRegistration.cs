using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Infrastructure;

/// <summary>Task 25: Every module's inbox plug-ins (kinds, sources, chat subjects, reverters), conversations, the router and the escalation standard. Kept together because these interfaces have one registration per module and their order is the order the inbox shows them.</summary>
internal static class InboxRegistration
{
    internal static IServiceCollection AddInbox(this IServiceCollection services, IConfiguration configuration)
    {
        // Monitoring exception cases in the Inbox (plan X1).
        services.AddInboxKind(ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseActions>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionConversationSubjectResolver>();
        // A reviewer's reply on an open monitoring or calendar case acknowledges it.
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationReplyHook, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseReplyAcknowledger>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseReverter>();
        // A reviewer's chat activity on a monitoring or calendar case acknowledges it (stops the SLA escalation).
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationActivityListener, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionCaseActivityAcknowledger>();
        // Calendar meeting-exception cases in the Inbox (plan C3).
        services.AddInboxKind(ONEVO.Application.Features.Calendar.Inbox.CalendarInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Calendar.Inbox.CalendarInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Calendar.Inbox.CalendarConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Calendar.Inbox.CalendarMeetingExceptionReverter>();
        // Attendance requests in the Inbox (plan A1).
        foreach (var kind in ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceAlertInbox>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceAlertReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider>(sp => sp.GetRequiredService<ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceInboxSourceProvider>());
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.AttendanceCorrectionReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.WorkModeChangeReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.LocationChangeReverter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.TimeAttendance.Inbox.DeviceChangeReverter>();
        // Leave in the Inbox (plan L1).
        services.AddInboxKind(ONEVO.Application.Features.Leave.Inbox.LeaveInboxKinds.Descriptor);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.Leave.Inbox.LeaveInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.Leave.Inbox.LeaveConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Revert.IInboxDecisionReverter, ONEVO.Application.Features.Leave.Inbox.LeaveDecisionReverter>();

        // Position access and seat increase approvals in the Inbox.
        foreach (var kind in ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider, ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.CoreHr.Onboarding.Inbox.OnboardingConversationSubjectResolver>();

        // Work Management aligned to the Inbox standard (plan W1).
        foreach (var kind in ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxKinds.Descriptors)
            services.AddInboxKind(kind);
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.WorkAlertEscalation>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationSweepSource, ONEVO.Application.Features.WorkManagement.Inbox.WorkMonitorAlertSweepSource>();
        services.AddScoped<ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxSourceProvider>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxSourceProvider>(sp => sp.GetRequiredService<ONEVO.Application.Features.WorkManagement.Inbox.WorkInboxSourceProvider>());
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver, ONEVO.Application.Features.WorkManagement.Inbox.WorkConversationSubjectResolver>();

        // Conversations (inbox item group chat) - shared module; sources plug in IConversationSubjectResolver.
        services.AddScoped<ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationRepository>();
        services.AddScoped<ONEVO.Application.Features.Conversations.RepositoryInterfaces.IConversationRepository>(sp =>
            sp.GetRequiredService<ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationRepository>());
        services.AddScoped<ONEVO.Application.Features.Conversations.RepositoryInterfaces.IConversationPeopleDirectory,
            ONEVO.Infrastructure.Persistence.Repositories.Conversations.EfConversationPeopleDirectory>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.IConversationAccess,
            ONEVO.Application.Features.Conversations.Services.ConversationAccess>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.ConversationResponseBuilder>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.ConversationNotifier>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.IConversationUnreadCounts,
            ONEVO.Application.Features.Conversations.Services.ConversationUnreadCounts>();
        // People chats: 1-on-1 chats and named groups (their own conversation sources), case cards placed into them.
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver,
            ONEVO.Application.Features.Conversations.Services.DirectConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Contracts.IConversationSubjectResolver,
            ONEVO.Application.Features.Conversations.Services.GroupConversationSubjectResolver>();
        services.AddScoped<ONEVO.Application.Features.Conversations.Services.IPeopleChatService,
            ONEVO.Application.Features.Conversations.Services.PeopleChatService>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Chats.ICaseGroupService, ONEVO.Application.Features.Inbox.Chats.CaseGroupService>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Chats.IInboxChatProjector, ONEVO.Application.Features.Inbox.Chats.InboxChatProjector>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Chats.IInboxChatService, ONEVO.Application.Features.Inbox.Chats.InboxChatService>();
        // Inbox router: fans out to every registered IInboxSourceProvider (one per module).
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxRouter, ONEVO.Application.Features.Inbox.Services.InboxRouter>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.InboxCallerResolver>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxPersonCaseService, ONEVO.Application.Features.Inbox.Services.InboxPersonCaseService>();
        // Inbox Standard: kind descriptors (each module adds its own with services.AddInboxKind), the decision
        // journal, the merged Steps timeline. Reverters register as IInboxDecisionReverter per (source, kind).
        services.AddSingleton<ONEVO.Application.Features.Inbox.Contracts.IInboxKindRegistry, ONEVO.Application.Features.Inbox.Contracts.InboxKindRegistry>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Contracts.IInboxDecisionJournal, ONEVO.Infrastructure.Persistence.Repositories.Inbox.EfInboxDecisionJournal>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxStepsBuilder, ONEVO.Application.Features.Inbox.Services.InboxStepsBuilder>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Services.IInboxChangeNotifier, ONEVO.Application.Features.Inbox.Services.OutboxInboxChangeNotifier>();
        // Inbox escalation standard: one policy registry (first match wins), the shared escalation recorder, and the
        // hourly SLA sweep over every module's IInboxEscalationSweepSource.
        services.AddScoped<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory,
            ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.EmployeeAuthorityResolverFactory>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy>(sp =>
            new ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ReportingLineThenHrPolicy(
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory>().CreateForTenant,
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>(),
                sp.GetRequiredService<ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces.IPermissionRepository>(),
                sp.GetRequiredService<IDateTimeProvider>()));
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy>(sp =>
            new ONEVO.Application.Features.Calendar.Inbox.ReportingLineThenLogPolicy(
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services.IEmployeeAuthorityResolverFactory>().CreateForTenant,
                sp.GetRequiredService<ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository>()));
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicy, ONEVO.Application.Features.WorkManagement.Inbox.WmHierarchyPolicy>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationPolicyRegistry, ONEVO.Application.Features.Inbox.Escalation.InboxEscalationPolicyRegistry>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationRecorder, ONEVO.Application.Features.Inbox.Escalation.InboxEscalationRecorder>();
        services.AddScoped<ONEVO.Application.Features.Inbox.Escalation.IInboxEscalationSweepSource, ONEVO.Application.Features.Monitoring.Exceptions.Inbox.ExceptionEscalationSweepSource>();
        // The Api host registers the SignalR-backed publisher; this is the fallback everywhere else.
        services.TryAddSingleton<ONEVO.Application.Features.Conversations.Contracts.IInboxRealtimePublisher,
            ONEVO.Application.Features.Conversations.Services.NullInboxRealtimePublisher>();

        return services;
    }
}
