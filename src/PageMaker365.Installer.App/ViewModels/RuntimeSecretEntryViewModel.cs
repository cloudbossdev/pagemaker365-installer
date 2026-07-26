using System.Security;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class RuntimeSecretEntryViewModel : ViewModelBase, IDisposable
{
    private SecureString? _value;
    private bool _containsOnlyPrintableAscii = true;
    private string _validationMessage;

    public RuntimeSecretEntryViewModel(RuntimeSecretInfo definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _validationMessage = $"Enter at least {definition.MinimumLength} character{(definition.MinimumLength == 1 ? "" : "s")}.";
    }

    public event EventHandler? ValueChanged;

    public RuntimeSecretInfo Definition { get; }
    public string Label => Definition.Label;
    public string Purpose => Definition.Purpose;
    public string AppSettingName => Definition.AppSettingName;
    public int MinimumLength => Definition.MinimumLength;
    public bool IsReady => _value is not null && _value.Length >= MinimumLength && _containsOnlyPrintableAscii;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public void SetValue(SecureString value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value?.Dispose();
        _value = value.Copy();
        _value.MakeReadOnly();
        _containsOnlyPrintableAscii = RuntimeSecretMaterial.IsPrintableAscii(_value);
        ValidationMessage = !_containsOnlyPrintableAscii
            ? "Use printable ASCII characters only."
            : IsReady
            ? "Ready for protected provisioning."
            : $"Enter at least {MinimumLength} character{(MinimumLength == 1 ? "" : "s")}.";
        OnPropertyChanged(nameof(IsReady));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public RuntimeSecretMaterial CreateMaterial()
    {
        if (!IsReady || _value is null)
        {
            throw new InvalidOperationException($"Protected value is incomplete for {AppSettingName}.");
        }

        return new RuntimeSecretMaterial(Definition, _value);
    }

    public void Clear()
    {
        _value?.Dispose();
        _value = null;
        _containsOnlyPrintableAscii = true;
        ValidationMessage = $"Enter at least {MinimumLength} character{(MinimumLength == 1 ? "" : "s")}.";
        OnPropertyChanged(nameof(IsReady));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _value?.Dispose();
        _value = null;
    }
}
