namespace PageMaker365.Installer.App.ViewModels;

public sealed class StepViewModel : ViewModelBase
{
    private string _statusLabel = "Pending";
    private string _statusBrush = "#2A355E";
    private bool _isAccessible;
    private bool _isCurrent;
    private string _rowBackground = "Transparent";
    private string _rowBorderBrush = "Transparent";

    public StepViewModel(int number, string name)
    {
        Number = number;
        Name = name;
    }

    public int Number { get; }
    public string Name { get; }

    public string StatusLabel
    {
        get => _statusLabel;
        set => SetProperty(ref _statusLabel, value);
    }

    public string StatusBrush
    {
        get => _statusBrush;
        set => SetProperty(ref _statusBrush, value);
    }

    public bool IsAccessible
    {
        get => _isAccessible;
        set => SetProperty(ref _isAccessible, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                RowBackground = value ? "#111B38" : "Transparent";
                RowBorderBrush = value ? "#3E55A0" : "Transparent";
            }
        }
    }

    public string RowBackground
    {
        get => _rowBackground;
        private set => SetProperty(ref _rowBackground, value);
    }

    public string RowBorderBrush
    {
        get => _rowBorderBrush;
        private set => SetProperty(ref _rowBorderBrush, value);
    }
}
