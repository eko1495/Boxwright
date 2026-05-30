namespace Boxwright.App.Services;

/// <summary>
/// Marshals an action onto the UI thread. Abstracted so view models can handle
/// background callbacks (e.g. a VM process exiting) without a direct dependency on
/// Avalonia's dispatcher — tests substitute a synchronous implementation.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread.</summary>
    void Post(Action action);
}
