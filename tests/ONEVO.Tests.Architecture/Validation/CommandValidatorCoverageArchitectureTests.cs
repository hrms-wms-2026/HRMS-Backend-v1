using System.Collections;
using System.Reflection;
using FluentValidation;
using MediatR;

namespace ONEVO.Tests.Architecture.Validation;

/// <summary>
/// Task 19: every MediatR command that carries a string (directly, in a string collection, or inside a nested
/// request type) must have a FluentValidation validator, so length limits and rich-text checks run before the
/// handler. Commands that are still to do are listed in <see cref="CommandValidatorAllowList"/>; that list may only
/// shrink: a listed command that gains a validator, or a listed name that no longer exists, fails the build so it is
/// removed from the list.
/// </summary>
public sealed class CommandValidatorCoverageArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ONEVO.Application.DependencyInjection).Assembly;

    [Fact]
    public void EveryCommandWithAStringProperty_HasAValidator_OrIsAllowListed()
    {
        var allowed = CommandValidatorAllowList.Commands.ToHashSet(StringComparer.Ordinal);
        var missing = CommandsWithStrings()
            .Where(c => !HasValidator(c))
            .Select(c => c.FullName!)
            .Where(name => !allowed.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "These commands take string input but have no AbstractValidator<TCommand>. Add a validator (see "
            + "ONEVO.Application.Common.Validation.ValidationRuleExtensions for the shared rules):\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void AllowList_OnlyShrinks_EntriesThatGainedAValidatorMustBeRemoved()
    {
        var byName = Commands().ToDictionary(t => t.FullName!, StringComparer.Ordinal);
        var stale = CommandValidatorAllowList.Commands
            .Where(name => !byName.TryGetValue(name, out var type) || HasValidator(type) || !HasStringInput(type))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "Remove these entries from CommandValidatorAllowList: they now have a validator, no longer take strings, "
            + "or no longer exist:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void AllowList_HasNoDuplicates()
    {
        var duplicates = CommandValidatorAllowList.Commands
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Scanner_FindsKnownCommandsWithAndWithoutValidators()
    {
        // Guards the scanner itself: if command or validator discovery silently broke, the coverage test would pass vacuously.
        var commands = CommandsWithStrings().ToList();
        Assert.True(commands.Count > 100, $"Expected more than 100 string-carrying commands, found {commands.Count}.");
        Assert.Contains(commands, c => c.Name == "CreateTaskCommentCommand");
        Assert.True(HasValidator(commands.Single(c => c.Name == "CreateTaskCommentCommand")));
        Assert.Contains(commands, c => c.Name == "SubmitLegalAcceptanceCommand");
    }

    /// <summary>The 30 highest-risk commands validated in task 19. Four of them carry only id lists, so the string scan
    /// above cannot see them; this pins every one so a validator cannot be deleted unnoticed.</summary>
    public static TheoryData<string> HighRiskCommands() =>
    [
        "Auth.Legal.Commands.SubmitLegalAcceptance.SubmitLegalAcceptanceCommand",
        "Auth.Login.Commands.AdminMfaConfirmSetup.ConfirmAdminMfaSetupCommand",
        "Auth.Login.Commands.AdminMfaVerify.VerifyAdminMfaCommand",
        "Auth.Login.Commands.BaseForgotPassword.BaseForgotPasswordCommand",
        "Auth.Login.Commands.MfaVerify.VerifyMfaCommand",
        "Auth.Login.Commands.RequestPasswordReset.RequestPasswordResetCommand",
        "Calendar.Commands.AddCalendarEventGuests.AddCalendarEventGuestsCommand",
        "Calendar.Commands.EditRecurringOccurrence.EditRecurringOccurrenceCommand",
        "Conversations.Commands.AddConversationParticipants.AddConversationParticipantsCommand",
        "Conversations.Commands.EditConversationMessage.EditConversationMessageCommand",
        "Conversations.Commands.PostConversationMessage.PostConversationMessageCommand",
        "CoreHr.BulkOnboarding.Commands.RequestBulkOnboardingFinalize.RequestBulkOnboardingFinalizeCommand",
        "CoreHr.Offboarding.Commands.CreateBypassRequest.CreateBypassRequestCommand",
        "CoreHr.Offboarding.Commands.RejectBypassRequest.RejectBypassRequestCommand",
        "CoreHr.Onboarding.Commands.RejectAccessGrantRequest.RejectAccessGrantRequestCommand",
        "Inbox.Chats.CreateGroupChatCommand",
        "Inbox.Chats.ForwardCaseCommand",
        "Inbox.Chats.ForwardMessageCommand",
        "Inbox.Chats.RenameGroupChatCommand",
        "Inbox.Chats.SetChatDescriptionCommand",
        "Inbox.Chats.StartCaseGroupCommand",
        "Leave.Approval.Commands.CreateApproverBackdatedLeaveCommand",
        "SharedPlatform.Webhooks.Stripe.ProcessStripeEventCommand",
        "TimeAttendance.Commands.AttendanceCorrections.ApproveAttendanceCorrectionCommand",
        "TimeAttendance.Commands.AttendanceCorrections.PreviewAttendanceCorrectionCommand",
        "TimeAttendance.Commands.AttendanceCorrections.RejectAttendanceCorrectionCommand",
        "TimeAttendance.Commands.AttendanceCorrections.RequestAttendanceCorrectionCommand",
        "WorkManagement.Sprints.Commands.SetSprintTasks.SetSprintTasksCommand",
        "WorkManagement.Tasks.Commands.CreateTaskComment.CreateTaskCommentCommand",
        "WorkManagement.Tasks.Commands.EditTaskComment.EditTaskCommentCommand",
    ];

    [Theory]
    [MemberData(nameof(HighRiskCommands))]
    public void HighRiskCommand_HasAValidator(string relativeName)
    {
        var type = ApplicationAssembly.GetType("ONEVO.Application.Features." + relativeName);
        Assert.True(type is not null, $"{relativeName} no longer exists; update this list.");
        Assert.True(HasValidator(type!), $"{relativeName} lost its validator.");
        Assert.DoesNotContain(type!.FullName!, CommandValidatorAllowList.Commands);
    }

    internal static IEnumerable<Type> Commands() =>
        LoadableTypes(ApplicationAssembly)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.Namespace?.StartsWith("ONEVO.Application.Features", StringComparison.Ordinal) == true)
            .Where(IsMediatRRequest);

    private static IEnumerable<Type> CommandsWithStrings() => Commands().Where(HasStringInput);

    private static bool IsMediatRRequest(Type t) =>
        typeof(IRequest).IsAssignableFrom(t)
        || t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

    private static readonly Lazy<HashSet<Type>> ValidatedTypes = new(() =>
        LoadableTypes(ApplicationAssembly)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet());

    private static bool HasValidator(Type command) => ValidatedTypes.Value.Contains(command);

    private static bool HasStringInput(Type type) => CarriesString(type, depth: 0, seen: []);

    private static bool CarriesString(Type type, int depth, HashSet<Type> seen)
    {
        if (depth > 3 || !seen.Add(type))
            return false;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.DeclaringType == typeof(object))
                continue;
            if (IsStringLike(property.PropertyType, depth, seen))
                return true;
        }
        return false;
    }

    private static bool IsStringLike(Type type, int depth, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string))
            return true;
        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments().Any(arg => IsStringLike(arg, depth + 1, seen));
        if (type.IsArray)
            return IsStringLike(type.GetElementType()!, depth + 1, seen);
        // Nested request shapes the API binds from the body (for example LegalAcceptanceItemInput).
        return type.IsClass && type.Namespace?.StartsWith("ONEVO.Application", StringComparison.Ordinal) == true
            && CarriesString(type, depth + 1, seen);
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
