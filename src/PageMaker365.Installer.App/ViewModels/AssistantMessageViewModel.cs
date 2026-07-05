using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class AssistantMessageViewModel
{
    public AssistantMessageViewModel(AssistantMessage model)
    {
        Model = model;
        Attachments = model.Attachments.Select(attachment => new AssistantAttachmentViewModel(attachment)).ToList();
    }

    public AssistantMessage Model { get; }
    public string Role => Model.Role;
    public string Content => Model.Content;
    public string Timestamp => Model.CreatedAt.ToLocalTime().ToString("g");
    public IReadOnlyList<AssistantAttachmentViewModel> Attachments { get; }
    public string AttachmentSummary => Attachments.Count == 0 ? "" : $"{Attachments.Count} attachment(s)";
}
