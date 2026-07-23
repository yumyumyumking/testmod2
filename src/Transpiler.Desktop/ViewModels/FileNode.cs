using System.Collections.ObjectModel;
using Transpiler.Desktop.Common;

namespace Transpiler.Desktop.ViewModels;

/// <summary>
/// One node in the IDE file-explorer tree: a folder or a file. Folders load their
/// children lazily on first expansion — a placeholder child makes the expand arrow
/// appear before the directory is scanned — so opening a large tree stays cheap and
/// inaccessible sub-folders never block the UI.
/// </summary>
public sealed class FileNode : ViewModelBase
{
    private readonly Func<string, IEnumerable<FileNode>> _childProvider;
    private bool _isExpanded;
    private bool _loaded;

    public FileNode(string name, string fullPath, bool isDirectory, Func<string, IEnumerable<FileNode>> childProvider)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _childProvider = childProvider ?? throw new ArgumentNullException(nameof(childProvider));

        if (isDirectory)
        {
            // A placeholder so the TreeView shows an expander; replaced on first expand.
            Children.Add(new FileNode("Loading…", string.Empty, isDirectory: false, childProvider));
        }
    }

    /// <summary>Display name (the file or folder name, not the full path).</summary>
    public string Name { get; }

    /// <summary>Absolute path this node represents.</summary>
    public string FullPath { get; }

    public bool IsDirectory { get; }

    public ObservableCollection<FileNode> Children { get; } = new();

    /// <summary>Two-way bound to the TreeViewItem; the first expansion loads real children.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
            {
                EnsureChildrenLoaded();
            }
        }
    }

    /// <summary>Populates real children on first expansion; safe to call repeatedly.</summary>
    public void EnsureChildrenLoaded()
    {
        if (_loaded || !IsDirectory)
        {
            return;
        }

        _loaded = true;
        Children.Clear();
        foreach (var child in _childProvider(FullPath))
        {
            Children.Add(child);
        }
    }
}
