using System.Windows;
using System.Windows.Threading;

namespace PageMaker365.Installer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "The requested installer action could not be completed. The installer has kept the current session open. Review the selected file or action and try again.",
            "PageMaker365 Installer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
