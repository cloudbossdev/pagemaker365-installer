using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class SharePointLibraryViewModel
{
    public SharePointLibraryViewModel(SharePointDocumentLibraryDiscovery library)
    {
        Name = string.IsNullOrWhiteSpace(library.Name) ? "Unnamed library" : library.Name;
        DriveType = string.IsNullOrWhiteSpace(library.DriveType) ? "Unknown" : library.DriveType;
        DriveId = string.IsNullOrWhiteSpace(library.DriveId) ? "Not discovered" : library.DriveId;
        WebUrl = string.IsNullOrWhiteSpace(library.WebUrl) ? "Not discovered" : library.WebUrl;
    }

    public string Name { get; }
    public string DriveType { get; }
    public string DriveId { get; }
    public string WebUrl { get; }
}
