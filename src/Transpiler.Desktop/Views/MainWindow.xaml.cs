using System.Collections.Specialized;
using System.Windows;
using Transpiler.Desktop.ViewModels;

namespace Transpiler.Desktop.Views;

/// <summary>
/// Code-behind for the main window. Contains view-only logic (console auto-scroll);
/// all behaviour lives in <see cref="MainViewModel"/>, which is injected by the container.
/// </summary>
public partial class MainWindow : Window
{
    private INotifyCollectionChanged? _observedEntries;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataContext = viewModel;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observedEntries is not null)
        {
            _observedEntries.CollectionChanged -= OnConsoleEntriesChanged;
            _observedEntries = null;
        }

        if (e.NewValue is MainViewModel viewModel)
        {
            _observedEntries = viewModel.ConsoleEntries;
            _observedEntries.CollectionChanged += OnConsoleEntriesChanged;
        }
    }

    private void OnConsoleEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || ConsoleList.Items.Count == 0)
        {
            return;
        }

        try
        {
            ConsoleList.ScrollIntoView(ConsoleList.Items[ConsoleList.Items.Count - 1]);
        }
        catch
        {
            // Auto-scroll is cosmetic; it must never take the app down (it runs on
            // every log line, including lines logged by the global error handler).
        }
    }
}
