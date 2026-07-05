using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class AssistantAttachmentViewModel
{
    public AssistantAttachmentViewModel(AssistantAttachment model)
    {
        Model = model;
    }

    public AssistantAttachment Model { get; }
    public string FileName => Model.FileName;
    public string Kind => Model.IsImage ? "Image" : Model.ContentType;
    public string PreviewPath => Model.IsImage ? Model.StoredPath : "";
    public string SizeLabel => FormatSize(Model.SizeBytes);
    public string UploadStatus => string.IsNullOrWhiteSpace(Model.UploadStatus) ? "Local" : Model.UploadStatus;

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes / 1024d / 1024d:0.#} MB";
    }
}
