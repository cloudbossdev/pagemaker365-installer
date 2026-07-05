using System.Windows;
using PageMaker365.Installer.App.ViewModels;

namespace PageMaker365.Installer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new InstallerWizardViewModel();
    }
}

