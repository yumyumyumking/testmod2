namespace Transpiler.Desktop.ViewModels;

/// <summary>A labelled choice for combo boxes (bound via DisplayMemberPath/SelectedValuePath).</summary>
public sealed class ChoiceOption<T>
{
    public ChoiceOption(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }
}
