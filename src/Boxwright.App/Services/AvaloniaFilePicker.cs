using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Boxwright.App.Services;

/// <summary>The production <see cref="IFilePicker"/>: Avalonia's storage file picker on the main window.</summary>
internal sealed class AvaloniaFilePicker : IFilePicker
{
    public async Task<string?> PickIsoAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an installer ISO",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Disc images") { Patterns = ["*.iso", "*.img"] },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
