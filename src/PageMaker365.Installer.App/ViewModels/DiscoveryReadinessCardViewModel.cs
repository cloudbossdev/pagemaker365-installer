namespace PageMaker365.Installer.App.ViewModels;

public sealed class DiscoveryReadinessCardViewModel
{
    public DiscoveryReadinessCardViewModel(string name, string statusLabel, string summary, string statusBrush)
    {
        Name = name;
        StatusLabel = statusLabel;
        Summary = summary;
        StatusBrush = statusBrush;
    }

    public string Name { get; }
    public string StatusLabel { get; }
    public string Summary { get; }
    public string StatusBrush { get; }
}
