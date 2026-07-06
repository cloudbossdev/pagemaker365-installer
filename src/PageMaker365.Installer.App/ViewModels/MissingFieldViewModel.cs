using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class MissingFieldViewModel
{
    public MissingFieldViewModel(OnboardingMissingField field)
    {
        FieldKey = string.IsNullOrWhiteSpace(field.FieldKey) ? "unknown" : field.FieldKey;
        Label = string.IsNullOrWhiteSpace(field.Label) ? FieldKey : field.Label;
        Source = string.IsNullOrWhiteSpace(field.Source) ? "Portal" : field.Source;
        Notes = string.IsNullOrWhiteSpace(field.Notes) ? "Required before package generation." : field.Notes;
        RequiredLabel = field.Required ? "Required" : "Optional";
        StatusBrush = field.Required ? "#FFB84D" : "#8290AA";
    }

    public string FieldKey { get; }
    public string Label { get; }
    public string Source { get; }
    public string Notes { get; }
    public string RequiredLabel { get; }
    public string StatusBrush { get; }
}
