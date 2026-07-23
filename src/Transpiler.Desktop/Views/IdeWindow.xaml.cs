using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Transpiler.Desktop.ViewModels;

namespace Transpiler.Desktop.Views;

/// <summary>
/// Code-behind for the file-explorer IDE. View-only concerns: the line-number gutter
/// and VS Code-style indentation guides — light vertical lines drawn from the visible
/// lines onto Canvas overlays — plus navigation from a console diagnostic to its source
/// line and unsaved-change guards. All editing and analysis state lives in
/// <see cref="IdeViewModel"/>; everything here is presentation.
/// </summary>
public partial class IdeWindow : Window
{
    /// <summary>Spaces per indentation level — matches the emitter's formatting profile.</summary>
    private const int IndentSize = 2;

    /// <summary>Cap on how far a blank line looks for a neighbouring indent (bounds cost).</summary>
    private const int MaxBlankScan = 200;

    private static readonly Brush GuideBrush =
        Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));

    private static readonly Brush LineNumberBrush =
        Freeze(new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)));

    private readonly IdeViewModel _viewModel;
    private double _charWidth;
    private double _pixelsPerDip = 1.0;

    public IdeWindow(IdeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // The overlays are redrawn whenever the visible text can have moved.
        Loaded += (_, _) => RenderOverlays();
        CodeBox.TextChanged += (_, _) => RenderOverlays();
        CodeBox.SizeChanged += (_, _) => RenderOverlays();
        CodeBox.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => RenderOverlays()));
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------ overlays (gutter + guides)

    private void EnsureMetrics()
    {
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_charWidth > 0)
        {
            return;
        }

        var typeface = new Typeface(CodeBox.FontFamily, CodeBox.FontStyle, CodeBox.FontWeight, CodeBox.FontStretch);
        var sample = new FormattedText(
            "0",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            CodeBox.FontSize,
            LineNumberBrush,
            _pixelsPerDip);

        _charWidth = sample.WidthIncludingTrailingWhitespace;
        if (_charWidth <= 0)
        {
            _charWidth = CodeBox.FontSize * 0.6; // defensive fallback for an unresolved font
        }
    }

    private void RenderOverlays()
    {
        try
        {
            GutterCanvas.Children.Clear();
            IndentCanvas.Children.Clear();

            if (!CodeBox.IsLoaded || CodeBox.ActualHeight <= 0)
            {
                return;
            }

            EnsureMetrics();

            var lineCount = CodeBox.LineCount;
            if (lineCount <= 0)
            {
                return;
            }

            var first = CodeBox.GetFirstVisibleLineIndex();
            var last = CodeBox.GetLastVisibleLineIndex();
            if (first < 0)
            {
                first = 0;
            }

            if (last < 0 || last >= lineCount)
            {
                last = lineCount - 1;
            }

            for (var line = first; line <= last; line++)
            {
                var lineStart = CodeBox.GetCharacterIndexFromLineIndex(line);
                if (lineStart < 0)
                {
                    continue;
                }

                var rect = CodeBox.GetRectFromCharacterIndex(lineStart);
                if (rect.IsEmpty)
                {
                    continue;
                }

                var height = rect.Height > 1 ? rect.Height : CodeBox.FontSize * 1.4;
                AddLineNumber(line, rect.Top);
                AddIndentGuides(line, rect.Left, rect.Top, height);
            }
        }
        catch
        {
            // The gutter and guides are cosmetic; never let rendering take the editor down.
        }
    }

    private void AddLineNumber(int line, double top)
    {
        var block = new TextBlock
        {
            Text = (line + 1).ToString(CultureInfo.InvariantCulture),
            FontFamily = CodeBox.FontFamily,
            FontSize = CodeBox.FontSize,
            Foreground = LineNumberBrush,
            Width = Math.Max(0, GutterCanvas.Width - 8),
            TextAlignment = TextAlignment.Right,
        };

        Canvas.SetTop(block, top);
        Canvas.SetLeft(block, 0);
        GutterCanvas.Children.Add(block);
    }

    private void AddIndentGuides(int line, double left, double top, double height)
    {
        var columns = IndentColumnsForLine(line);
        for (var level = 1; level * IndentSize <= columns; level++)
        {
            var x = left + (level * IndentSize * _charWidth);
            if (x < 0 || x > IndentCanvas.ActualWidth)
            {
                continue;
            }

            IndentCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = top + height,
                Stroke = GuideBrush,
                StrokeThickness = 1,
                SnapsToDevicePixels = true,
            });
        }
    }

    /// <summary>
    /// Leading-whitespace columns for a line, with VS Code-style continuation: a blank
    /// line inherits the smaller indent of the nearest non-blank lines above and below,
    /// so guides run unbroken through the blank lines inside a block.
    /// </summary>
    private int IndentColumnsForLine(int line)
    {
        var text = SafeLineText(line);
        if (!IsBlank(text))
        {
            return LeadingColumns(text);
        }

        var above = NearestNonBlankColumns(line - 1, -1);
        var below = NearestNonBlankColumns(line + 1, +1);
        if (above < 0 && below < 0)
        {
            return 0;
        }

        if (above < 0)
        {
            return below;
        }

        if (below < 0)
        {
            return above;
        }

        return Math.Min(above, below);
    }

    private int NearestNonBlankColumns(int start, int direction)
    {
        var lineCount = CodeBox.LineCount;
        var scanned = 0;
        for (var line = start; line >= 0 && line < lineCount && scanned < MaxBlankScan; line += direction, scanned++)
        {
            var text = SafeLineText(line);
            if (!IsBlank(text))
            {
                return LeadingColumns(text);
            }
        }

        return -1;
    }

    private string SafeLineText(int line)
    {
        try
        {
            return CodeBox.GetLineText(line) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);

    private static int LeadingColumns(string text)
    {
        var columns = 0;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                columns++;
            }
            else if (ch == '\t')
            {
                columns += IndentSize;
            }
            else
            {
                break;
            }
        }

        return columns;
    }

    // ------------------------------------------------------------- navigation

    private void OnProblemActivated(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not DiagnosticItem item || item.Line <= 0)
        {
            return;
        }

        var lineIndex = Math.Min(item.Line - 1, Math.Max(0, CodeBox.LineCount - 1));
        var start = CodeBox.GetCharacterIndexFromLineIndex(lineIndex);
        var length = CodeBox.GetLineLength(lineIndex);

        CodeBox.Focus();
        CodeBox.Select(Math.Max(0, start), Math.Max(0, length));
        CodeBox.ScrollToLine(lineIndex);
    }

    // ------------------------------------------------------------ file opening

    private void OnFileTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Directories keep the default TreeView expand/collapse behaviour.
        if (FileTree.SelectedItem is not FileNode node || node.IsDirectory)
        {
            return;
        }

        if (ConfirmDiscardIfDirty())
        {
            _viewModel.OpenFile(node.FullPath);
        }
    }

    private bool ConfirmDiscardIfDirty()
    {
        if (!_viewModel.IsDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(
            "The current file has unsaved changes. Discard them?",
            "Unsaved changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsDirty && !ConfirmDiscardIfDirty())
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
