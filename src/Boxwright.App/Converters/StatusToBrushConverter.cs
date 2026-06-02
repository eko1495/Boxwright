using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Boxwright.App.ViewModels;

namespace Boxwright.App.Converters;

/// <summary>
/// Maps a <see cref="VmStatus"/> to the fill brush of its status pill. The colors are
/// fixed (not theme-resolved) so the pill reads identically on light and dark surfaces
/// and never needs re-evaluating when the app theme toggles. The pill's label is the
/// view model's <c>StatusText</c>; this only supplies the background. One-way.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    /// <summary>Shared instance for use from XAML via <c>{x:Static}</c>.</summary>
    public static StatusToBrushConverter Instance { get; } = new();

    private static readonly IBrush Running = new ImmutableSolidColorBrush(Color.Parse("#2F9E44")); // green
    private static readonly IBrush Paused = new ImmutableSolidColorBrush(Color.Parse("#E8920C")); // amber
    private static readonly IBrush Busy = new ImmutableSolidColorBrush(Color.Parse("#1C7ED6")); // blue
    private static readonly IBrush Stopped = new ImmutableSolidColorBrush(Color.Parse("#6B7280")); // slate

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is VmStatus status
            ? status switch
            {
                VmStatus.Running => Running,
                VmStatus.Paused => Paused,
                VmStatus.Starting or VmStatus.Stopping => Busy,
                _ => Stopped,
            }
            : Stopped;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
