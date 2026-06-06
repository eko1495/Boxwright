using Boxwright.App.Services;
using Boxwright.Core;

namespace Boxwright.App.ViewModels;

/// <summary>Marshals <see cref="IProgress{T}"/> ISO-download reports onto the UI thread via <see cref="IUiDispatcher"/>.</summary>
internal sealed class DispatchedProgress(IUiDispatcher dispatcher, Action<IsoDownloadProgress> callback)
    : IProgress<IsoDownloadProgress>
{
    public void Report(IsoDownloadProgress value) => dispatcher.Post(() => callback(value));
}
