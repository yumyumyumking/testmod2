using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Transpiler.Desktop.ViewModels;

namespace Transpiler.Desktop.Views;

/// <summary>
/// Code-behind for the live editor. View-only logic: red squiggle underlines drawn
/// from diagnostic spans, hover tooltips over squiggled text, Tab autocomplete fed by
/// the view model, and problem-list navigation. All analysis lives in
/// <see cref="EditorViewModel"/>; everything here is presentation.
/// </summary>
public partial class EditorWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x6A, 0x00));

    private readonly EditorViewModel _viewModel;
    private readonly ToolTip _hoverTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private DiagnosticItem? _hoverItem;
    private int _completionStart = -1;

    public EditorWindow(EditorViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _viewModel.DiagnosticsRefreshed += (_, _) => RedrawSquiggles();
        SourceBox.TextChanged += (_, _) =>
        {
            SquiggleCanvas.Children.Clear(); // spans are stale until the next analysis
            CloseCompletion();
        };
        SourceBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => RedrawSquiggles()));
        SourceBox.SizeChanged += (_, _) => RedrawSquiggles();
        SourceBox.MouseMove += OnSourceMouseMove;
        SourceBox.MouseLeave += (_, _) => CloseHoverTip();
        SourceBox.PreviewKeyDown += OnSourcePreviewKeyDown;
        SourceBox.LostKeyboardFocus += (_, _) => CloseCompletion();
        SourceBox.ToolTip = _hoverTip;
        _hoverTip.IsOpen = false;
        ToolTipService.SetIsEnabled(SourceBox, false); // shown manually, only over squiggles
    }

    // ------------------------------------------------------------- squiggles

    private void RedrawSquiggles()
    {
        try
        {
            SquiggleCanvas.Children.Clear();
            if (!SourceBox.IsLoaded)
            {
                return;
            }

            var textLength = SourceBox.Text.Length;
            foreach (var item in _viewModel.Problems)
            {
                if (item.Start < 0 || item.Start >= textLength)
                {
                    continue;
                }

                var start = item.Start;
                var end = Math.Min(start + Math.Max(item.Length, 1), textLength);

                // Clamp the underline to the first line of the span.
                var line = SourceBox.GetLineIndexFromCharacterIndex(start);
                if (line >= 0)
                {
                    var lineEnd = SourceBox.GetCharacterIndexFromLineIndex(line) + SourceBox.GetLineLength(line);
                    end = Math.Min(end, Math.Max(start + 1, lineEnd));
                }

                var startRect = SourceBox.GetRectFromCharacterIndex(start);
                var endRect = SourceBox.GetRectFromCharacterIndex(end - 1, trailingEdge: true);
                if (startRect.IsEmpty || endRect.IsEmpty)
                {
                    continue;
                }

                var y = startRect.Bottom - 1;
                if (y < 0 || startRect.Top > SourceBox.ActualHeight)
                {
                    continue; // scrolled out of view
                }

                var x1 = startRect.Left;
                var x2 = Math.Max(endRect.Right, x1 + 8);
                SquiggleCanvas.Children.Add(BuildWave(x1, x2, y, item.IsError ? ErrorBrush : WarningBrush));
            }
        }
        catch
        {
            // Squiggles are cosmetic; never let rendering take the editor down.
        }
    }

    private static Polyline BuildWave(double x1, double x2, double y, Brush brush)
    {
        var points = new PointCollection();
        var up = true;
        for (var x = x1; x <= x2; x += 3)
        {
            points.Add(new Point(x, up ? y : y + 2.5));
            up = !up;
        }

        return new Polyline
        {
            Points = points,
            Stroke = brush,
            StrokeThickness = 1.2,
        };
    }

    // ---------------------------------------------------------- hover tooltip

    private void OnSourceMouseMove(object sender, MouseEventArgs e)
    {
        try
        {
            var index = SourceBox.GetCharacterIndexFromPoint(e.GetPosition(SourceBox), snapToText: false);
            DiagnosticItem? hit = null;
            if (index >= 0)
            {
                hit = _viewModel.Problems.FirstOrDefault(p =>
                    p.Start >= 0 && index >= p.Start && index < p.Start + Math.Max(p.Length, 1));
            }

            if (ReferenceEquals(hit, _hoverItem))
            {
                return;
            }

            _hoverItem = hit;
            if (hit is null)
            {
                CloseHoverTip();
            }
            else
            {
                _hoverTip.Content = hit.Tooltip;
                _hoverTip.IsOpen = true;
            }
        }
        catch
        {
            CloseHoverTip();
        }
    }

    private void CloseHoverTip()
    {
        _hoverItem = null;
        _hoverTip.IsOpen = false;
    }

    // ------------------------------------------------------- tab autocomplete

    private void OnSourcePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CompletionPopup.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Tab:
                case Key.Enter:
                    AcceptCompletion();
                    e.Handled = true;
                    return;
                case Key.Down:
                    CompletionList.SelectedIndex = Math.Min(CompletionList.SelectedIndex + 1, CompletionList.Items.Count - 1);
                    CompletionList.ScrollIntoView(CompletionList.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Up:
                    CompletionList.SelectedIndex = Math.Max(CompletionList.SelectedIndex - 1, 0);
                    CompletionList.ScrollIntoView(CompletionList.SelectedItem);
                    e.Handled = true;
                    return;
                case Key.Escape:
                    CloseCompletion();
                    e.Handled = true;
                    return;
                default:
                    CloseCompletion();
                    return;
            }
        }

        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var caret = SourceBox.CaretIndex;
        var text = SourceBox.Text;
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        if (start == caret)
        {
            return; // no word before the caret: let Tab insert whitespace
        }

        var prefix = text[start..caret];
        var matches = _viewModel.GetCompletions(prefix);
        if (matches.Count == 0)
        {
            return;
        }

        _completionStart = start;
        if (matches.Count == 1)
        {
            InsertCompletion(matches[0]);
            e.Handled = true;
            return;
        }

        CompletionList.ItemsSource = matches;
        CompletionList.SelectedIndex = 0;

        var caretRect = SourceBox.GetRectFromCharacterIndex(caret);
        CompletionPopup.HorizontalOffset = caretRect.Left;
        CompletionPopup.VerticalOffset = caretRect.Bottom + 2;
        CompletionPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnCompletionClicked(object sender, MouseButtonEventArgs e) => AcceptCompletion();

    private void AcceptCompletion()
    {
        if (CompletionList.SelectedItem is string word)
        {
            InsertCompletion(word);
        }

        CloseCompletion();
    }

    private void InsertCompletion(string word)
    {
        // Capture the anchor BEFORE CloseCompletion resets it to -1 — selecting
        // from a negative start throws ArgumentOutOfRangeException.
        var start = _completionStart;
        if (start < 0 || start > SourceBox.Text.Length)
        {
            return;
        }

        var caret = SourceBox.CaretIndex;
        CloseCompletion();
        SourceBox.Select(start, Math.Max(0, caret - start));
        SourceBox.SelectedText = word;
        SourceBox.CaretIndex = start + word.Length;
        SourceBox.Select(SourceBox.CaretIndex, 0);
    }

    private void CloseCompletion()
    {
        CompletionPopup.IsOpen = false;
        CompletionList.ItemsSource = null;
        _completionStart = -1;
    }

    // ------------------------------------------------------------- navigation

    private void OnProblemActivated(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not DiagnosticItem item || item.Line <= 0)
        {
            return;
        }

        var lineIndex = Math.Min(item.Line - 1, Math.Max(0, SourceBox.LineCount - 1));
        var start = SourceBox.GetCharacterIndexFromLineIndex(lineIndex);
        var length = SourceBox.GetLineLength(lineIndex);

        SourceBox.Focus();
        SourceBox.Select(Math.Max(0, start), Math.Max(0, length));
        SourceBox.ScrollToLine(lineIndex);
    }
}
