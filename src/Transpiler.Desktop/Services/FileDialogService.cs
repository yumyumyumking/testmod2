using Microsoft.Win32;

namespace Transpiler.Desktop.Services;

/// <summary>
/// Win32-backed implementation of <see cref="IFileDialogService"/>.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public IReadOnlyList<string> PickFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public string? PickFolder(string title, string? initialPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string title, string filter, string? suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedName ?? string.Empty,
            AddExtension = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
