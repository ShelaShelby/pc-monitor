using System.Windows;
using System.Windows.Media;
using PcMonitor.Core;

namespace PcMonitor.App.Controls;

/// <summary>60-second line chart. Gaps (n/a samples) break the line instead of being interpolated.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<HistoryPoint>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<HistoryPoint>? Points { get => (IReadOnlyList<HistoryPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double WindowSeconds { get; set; } = 60;

    private static readonly Pen GridPen = MakePen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1);

    private static Pen MakePen(Color c, double w)
    {
        var p = new Pen(new SolidColorBrush(c), w);
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4)
            return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawLine(GridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        dc.DrawLine(GridPen, new Point(0, h / 2), new Point(w, h / 2));

        IReadOnlyList<HistoryPoint>? pts = Points;
        if (pts is null || pts.Count == 0)
            return;
        double[] values = pts.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToArray();
        if (values.Length == 0)
            return;

        double lo = double.IsNaN(Minimum) ? values.Min() : Minimum;
        double hi = double.IsNaN(Maximum) ? values.Max() : Maximum;
        if (double.IsNaN(Minimum) || double.IsNaN(Maximum))
        {
            // Auto range with a little headroom; flat lines sit in the middle.
            double span = hi - lo;
            double pad = span < 1e-9 ? Math.Max(1, Math.Abs(hi) * 0.1) : span * 0.15;
            if (double.IsNaN(Minimum)) lo -= pad;
            if (double.IsNaN(Maximum)) hi += pad;
        }
        if (hi - lo < 1e-9)
            hi = lo + 1;

        double latest = pts[^1].TimeSeconds;
        double start = latest - WindowSeconds;
        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        Brush fill = Stroke.Clone();
        fill.Opacity = 0.15;
        fill.Freeze();

        var geometry = new StreamGeometry();
        var area = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        using (StreamGeometryContext a = area.Open())
        {
            bool inSegment = false;
            Point first = default, last = default;
            foreach (HistoryPoint p in pts)
            {
                if (!p.Value.HasValue)
                {
                    if (inSegment)
                        CloseArea(a, first, last, h);
                    inSegment = false;
                    continue;
                }
                double x = (p.TimeSeconds - start) / WindowSeconds * w;
                double y = h - (Math.Clamp(p.Value.Value, lo, hi) - lo) / (hi - lo) * (h - 2) - 1;
                var pt = new Point(x, y);
                if (!inSegment)
                {
                    g.BeginFigure(pt, false, false);
                    a.BeginFigure(new Point(x, h), true, true);
                    a.LineTo(pt, false, false);
                    first = pt;
                    inSegment = true;
                }
                else
                {
                    g.LineTo(pt, true, true);
                    a.LineTo(pt, false, false);
                }
                last = pt;
            }
            if (inSegment)
                CloseArea(a, first, last, h);
        }
        geometry.Freeze();
        area.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void CloseArea(StreamGeometryContext a, Point first, Point last, double h) =>
        a.LineTo(new Point(last.X, h), false, false);
}
