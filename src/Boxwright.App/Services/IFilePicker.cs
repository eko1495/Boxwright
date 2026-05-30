namespace Boxwright.App.Services;

/// <summary>
/// Lets a view model ask the user to pick a file without depending on Avalonia's
/// storage APIs directly (tests substitute a fake). Returns the local path, or null
/// if the user cancelled.
/// </summary>
public interface IFilePicker
{
    /// <summary>Prompts for an installer ISO / disc image. Returns its local path, or null if cancelled.</summary>
    Task<string?> PickIsoAsync();
}
