using Avalonia.Platform.Storage;

namespace TrispotQR.UI.Services;

/// <summary>
/// Turns the Win32 filter string that <see cref="TrispotQR.ViewModels.IDialogService"/> carries
/// into Avalonia file types.
///
/// The interface was extracted from a WPF app and still speaks "Name|*.ext|Name|*.ext". That is
/// debt in a project meant to be toolkit-neutral, and the right fix is a structured type on the
/// interface — but changing the interface is Phase 2c work at the earliest, and absorbing the
/// format here costs one small, well-tested function in the meantime.
/// </summary>
public static class FileFilter
{
    public static IReadOnlyList<FilePickerFileType> Parse(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        var types = new List<FilePickerFileType>(parts.Length / 2);

        // Pairs, so a trailing unpaired name is ignored rather than throwing.
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i])
            {
                Patterns = [.. parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            });
        }

        return types;
    }
}
