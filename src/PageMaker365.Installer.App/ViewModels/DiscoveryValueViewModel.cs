namespace PageMaker365.Installer.App.ViewModels;

public sealed class DiscoveryValueViewModel
{
    public DiscoveryValueViewModel(string section, string label, string value)
    {
        Section = section;
        Label = label;
        Value = string.IsNullOrWhiteSpace(value) ? "Not discovered" : value;
    }

    public string Section { get; }
    public string Label { get; }
    public string Value { get; }
}
