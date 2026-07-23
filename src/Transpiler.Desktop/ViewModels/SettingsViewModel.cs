using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Transpiler.Desktop.Common;
using Transpiler.Desktop.Services;

namespace Transpiler.Desktop.ViewModels;

/// <summary>
/// View model for the Settings dialog. Works on a clone of the live settings so
/// Cancel discards edits; the owning view model adopts <see cref="Settings"/> on save.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IFileDialogService _dialogService;
    private string _validationMessage = string.Empty;

    public SettingsViewModel(
        TranspilerSettings settings,
        IFileDialogService dialogService,
        IReadOnlyList<string>? languageNames = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        Languages = languageNames is { Count: > 0 } ? languageNames : new[] { "CLX", "CL" };

        Verbosities = new[]
        {
            new ChoiceOption<LogLevel>(LogLevel.Trace, "Trace (most detailed)"),
            new ChoiceOption<LogLevel>(LogLevel.Debug, "Debug"),
            new ChoiceOption<LogLevel>(LogLevel.Information, "Information"),
            new ChoiceOption<LogLevel>(LogLevel.Warning, "Warnings only"),
            new ChoiceOption<LogLevel>(LogLevel.Error, "Errors only"),
        };

        BrowseLanguagesFolderCommand = new RelayCommand(BrowseLanguagesFolder);
    }

    /// <summary>The edited settings instance (a clone of the live settings).</summary>
    public TranspilerSettings Settings { get; }

    /// <summary>Every registered language name — both the source and target dropdowns list these.</summary>
    public IReadOnlyList<string> Languages { get; }

    public IReadOnlyList<ChoiceOption<LogLevel>> Verbosities { get; }

    public ICommand BrowseLanguagesFolderCommand { get; }

    public string LanguagesFolder
    {
        get => Settings.LanguagesFolder;
        set
        {
            if (Settings.LanguagesFolder != value)
            {
                Settings.LanguagesFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public string SourceLanguage
    {
        get => Settings.SourceLanguage;
        set
        {
            if (!string.IsNullOrEmpty(value) && Settings.SourceLanguage != value)
            {
                Settings.SourceLanguage = value;
                OnPropertyChanged();
            }
        }
    }

    public string TargetLanguage
    {
        get => Settings.TargetLanguage;
        set
        {
            if (!string.IsNullOrEmpty(value) && Settings.TargetLanguage != value)
            {
                Settings.TargetLanguage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CreateBackup
    {
        get => Settings.CreateBackup;
        set
        {
            if (Settings.CreateBackup != value)
            {
                Settings.CreateBackup = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OverwriteExisting
    {
        get => Settings.OverwriteExisting;
        set
        {
            if (Settings.OverwriteExisting != value)
            {
                Settings.OverwriteExisting = value;
                OnPropertyChanged();
            }
        }
    }

    public LogLevel Verbosity
    {
        get => Settings.Verbosity;
        set
        {
            if (Settings.Verbosity != value)
            {
                Settings.Verbosity = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Shown in red at the bottom of the dialog when validation fails.</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>Validates the edited settings; called by the dialog's Save button.</summary>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(LanguagesFolder))
        {
            ValidationMessage = "The languages folder must not be empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SourceLanguage) || string.IsNullOrWhiteSpace(TargetLanguage))
        {
            ValidationMessage = "A source and a target language must both be selected.";
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }

    private void BrowseLanguagesFolder()
    {
        var initial = Path.IsPathRooted(LanguagesFolder) ? LanguagesFolder : AppContext.BaseDirectory;
        var folder = _dialogService.PickFolder("Select the languages folder", initial);
        if (folder is not null)
        {
            LanguagesFolder = folder;
        }
    }
}
