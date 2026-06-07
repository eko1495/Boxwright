using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Boxwright.App.Controls;

/// <summary>
/// A tiny dependency-free line chart (ADR-0019): draws the <see cref="Values"/> series as a polyline
/// normalized across the control bounds. <see cref="Maximum"/> pins the top of the scale (e.g. 100 for a
/// CPU %); left at <see cref="double.NaN"/> it auto-scales to the series maximum. Redraws whenever the
/// series, stroke, or scale changes.
/// </summary>
public sealed class Sparkline : Control
{
    /// <summary>The data series to plot (oldest first). Fewer than 2 points draws nothing.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    /// <summary>The line color.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.DodgerBlue);

    /// <summary>Fixed top-of-scale value; <see cref="double.NaN"/> (default) auto-scales to the series max.</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), double.NaN);

    static Sparkline() => AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, MaximumProperty);

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        IReadOnlyList<double>? values = Values;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (values is null || values.Count < 2 || width <= 0 || height <= 0)
        {
            return;
        }

        double max = Maximum;
        if (double.IsNaN(max) || max <= 0)
        {
            max = 0;
            foreach (double v in values)
            {
                max = Math.Max(max, v);
            }

            if (max <= 0)
            {
                max = 1; // a flat zero series draws along the baseline
            }
        }

        double stepX = width / (values.Count - 1);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                double norm = Math.Clamp(values[i] / max, 0, 1);
                var point = new Point(i * stepX, height - (norm * height));
                if (i == 0)
                {
                    ctx.BeginFigure(point, isFilled: false);
                }
                else
                {
                    ctx.LineTo(point);
                }
            }

            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, new Pen(Stroke ?? Brushes.DodgerBlue, 1.5), geometry);
    }
}
