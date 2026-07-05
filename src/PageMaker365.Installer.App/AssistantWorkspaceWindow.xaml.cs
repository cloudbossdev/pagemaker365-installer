using System.Windows;
using System.Windows.Input;
using PageMaker365.Installer.App.ViewModels;

namespace PageMaker365.Installer.App;

public partial class AssistantWorkspaceWindow : Window
{
    public AssistantWorkspaceWindow(AssistantWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not AssistantWorkspaceViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await viewModel.AddDroppedFilesAsync(files);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (Clipboard.ContainsImage() && DataContext is AssistantWorkspaceViewModel viewModel)
        {
            viewModel.PasteClipboardImageCommand.Execute(null);
            e.Handled = true;
        }
    }
}
