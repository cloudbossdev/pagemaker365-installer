using System.Windows;
using System.Windows.Controls;
using PageMaker365.Installer.App.ViewModels;

namespace PageMaker365.Installer.App;

public partial class MainWindow : Window
{
    private const double CompactWidth = 940;
    private const double MediumWidth = 1120;
    private const double ShortHeight = 650;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new InstallerWizardViewModel();
        Loaded += (_, _) => UpdateShellLayout();
        SizeChanged += OnWindowSizeChanged;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShellLayout();
    }

    private void UpdateShellLayout()
    {
        var compact = ActualWidth > 0 && ActualWidth < CompactWidth;
        var medium = ActualWidth > 0 && ActualWidth < MediumWidth;
        var shortWindow = ActualHeight > 0 && ActualHeight < ShortHeight;

        LeftColumn.Width = new GridLength(compact ? 220 : medium ? 250 : 280);
        RightColumn.Width = compact
            ? new GridLength(0)
            : new GridLength(medium ? 270 : 320);

        RightRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactGuidanceCard.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        HeaderBar.Padding = compact || shortWindow ? new Thickness(20, 14, 20, 14) : new Thickness(28, 22, 28, 22);
        FooterBar.Padding = compact || shortWindow ? new Thickness(14, 12, 14, 12) : new Thickness(22);
        LeftRail.Padding = compact || shortWindow ? new Thickness(14, 16, 14, 16) : new Thickness(22, 24, 22, 24);
        RightRail.Padding = shortWindow ? new Thickness(18, 16, 18, 16) : new Thickness(24);
        CenterContent.Margin = compact || shortWindow ? new Thickness(20) : new Thickness(30);
        HeaderWordmark.Width = compact ? 210 : 250;

        WelcomeChoiceGrid.Columns = compact ? 1 : 2;
        SetupChoiceCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 9, 12);
        RemovalChoiceCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(9, 0, 0, 12);

        OnboardingActionsGrid.Columns = compact ? 2 : 4;
        CompactGuidanceActions.Columns = ActualWidth > 0 && ActualWidth < 820 ? 1 : 3;
        RemovalPreviewGrid.Columns = compact ? 1 : 3;

        FooterStatusText.MaxWidth = compact ? Math.Max(260, ActualWidth - 340) : double.PositiveInfinity;
    }
}
