using System.Windows;
using Transpiler.Desktop.ViewModels;

namespace Transpiler.Desktop.Views;

/// <summary>
/// Code-behind for the Settings dialog. Save validates through the view model and
/// closes with a positive dialog result; Cancel closes via IsCancel.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && viewModel.Validate())
        {
            DialogResult = true;
        }
    }
}
