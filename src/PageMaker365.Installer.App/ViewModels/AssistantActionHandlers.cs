namespace PageMaker365.Installer.App.ViewModels;

public sealed class AssistantActionHandlers
{
    public Func<Task<string>>? CreateSupportBundleAsync { get; init; }
    public Func<Task<string>>? DraftAdminMessageAsync { get; init; }
    public Func<Task<string>>? RerunPreflightAsync { get; init; }
}
