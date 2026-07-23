namespace Transpiler.Desktop.Services;

/// <summary>
/// Abstracts Win32 file/folder dialogs so view models stay testable.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows a multi-select open dialog. Returns the chosen paths, or an empty
    /// array when the user cancels.
    /// </summary>
    /// <param name="title">Dialog caption.</param>
    /// <param name="filter">Win32 filter string, e.g. "CLX Files (*.clx)|*.clx".</param>
    IReadOnlyList<string> PickFiles(string title, string filter);

    /// <summary>Shows a folder picker. Returns null when the user cancels.</summary>
    string? PickFolder(string title, string? initialPath);

    /// <summary>Shows a save dialog. Returns null when the user cancels.</summary>
    string? PickSaveFile(string title, string filter, string? suggestedName);
}
