using System.IO;
using System.Windows;
using Microsoft.Win32;
using TrispotQR.ViewModels;

namespace TrispotQR.App.Services;

/// <inheritdoc />
public sealed class DialogService : IDialogService
{
    public string? AskForSavePath(
        string title, string filter, string defaultExtension, string suggestedName, string? directory)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            FileName = suggestedName,
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? AskForImage(string? directory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a logo image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|PNG (best, supports transparency)|*.png|All files|*.*",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? AskForText(string title, string prompt, string initialValue)
    {
        var window = new Views.TextPromptWindow(title, prompt, initialValue)
        {
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true ? window.Value : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe)
    {
        var window = new Views.ConfirmWindow(heading, message, proceedLabel, defaultToProceed, severe)
        {
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInformation(string title, string message) =>
        MessageBox.Show(Owner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static Window Owner() => Application.Current?.MainWindow!;
}
