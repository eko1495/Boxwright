using Avalonia.Threading;

namespace Boxwright.App.Services;

/// <summary>The production <see cref="IUiDispatcher"/>: posts to Avalonia's UI thread.</summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
