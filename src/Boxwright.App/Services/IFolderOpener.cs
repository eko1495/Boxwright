namespace Boxwright.App.Services;

/// <summary>
/// Reveals a folder in the OS file manager (Explorer / Finder / the Linux default file
/// manager). A desktop-shell/presentation concern, so it lives in the App alongside the
/// other platform seams (e.g. <see cref="IFilePicker"/>), not in Core.
/// </summary>
public interface IFolderOpener
{
    /// <summary>Opens <paramref name="path"/> in the platform file manager. Best-effort — never throws.</summary>
    void OpenFolder(string path);
}
