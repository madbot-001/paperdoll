using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Paperdoll.App;

public enum UnsavedChoice
{
    Save,
    Discard,
}

/// <summary>Asks whether to save a character's changes before they would be lost. Closing it cancels.</summary>
public partial class UnsavedWindow : Window
{
    public UnsavedWindow() => InitializeComponent();

    /// <param name="what">What is about to happen, such as "opening another character".</param>
    public UnsavedWindow(string name, string what) : this() =>
        MessageText.Text = $"{name} has changes that are not saved to a file, and {what} would lose them. Save them first?";

    private void OnSave(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Save);

    private void OnDiscard(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Discard);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
