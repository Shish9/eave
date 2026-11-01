using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClaudeIsland;

/// The island's outline. Notch: hangs from the top edge with concave (flared) top corners and a rounded bottom.
/// Floating: a plain rounded pill. BodyWidth is the width between the flares.
public class NotchShape : Shape
{
    static FrameworkPropertyMetadata Meta(object def) =>
        new(def, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender);

    public static readonly DependencyProperty BodyWidthProperty =
        DependencyProperty.Register(nameof(BodyWidth), typeof(double), typeof(NotchShape), Meta(190.0));
    public static readonly DependencyProperty BodyHeightProperty =
        DependencyProperty.Register(nameof(BodyHeight), typeof(double), typeof(NotchShape), Meta(38.0));
    public static readonly DependencyProperty FlareRadiusProperty =
        DependencyProperty.Register(nameof(FlareRadius), typeof(double), typeof(NotchShape), Meta(16.0));
    public static readonly DependencyProperty FloatingProperty =
        DependencyProperty.Register(nameof(Floating), typeof(bool), typeof(NotchShape), Meta(false));

    public double BodyWidth { get => (double)GetValue(BodyWidthProperty); set => SetValue(BodyWidthProperty, value); }
    public double BodyHeight { get => (double)GetValue(BodyHeightProperty); set => SetValue(BodyHeightProperty, value); }
    public double FlareRadius { get => (double)GetValue(FlareRadiusProperty); set => SetValue(FlareRadiusProperty, value); }
    public bool Floating { get => (bool)GetValue(FloatingProperty); set => SetValue(FloatingProperty, value); }

    protected override Geometry DefiningGeometry
    {
        get
        {
            double h = Math.Max(BodyHeight, 28);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                if (Floating)
                {
                    double w = Math.Max(BodyWidth, 60);
                    double r = Math.Min(h / 2, h > 52 ? 26 : 20);
                    c.BeginFigure(new Point(r, 0), true, true);
                    c.LineTo(new Point(w - r, 0), true, false);
                    c.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(w, h - r), true, false);
                    c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(r, h), true, false);
                    c.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(0, r), true, false);
                    c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    double rt = Math.Clamp(FlareRadius, 2, 30);
                    double w = Math.Max(BodyWidth, 2 * rt + 8);
                    double rb = Math.Min(h > 52 ? 26 : 16, h - rt - 2);

                    c.BeginFigure(new Point(0, 0), true, true);
                    c.ArcTo(new Point(rt, rt), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);              // top-left flare
                    c.LineTo(new Point(rt, h - rb), true, false);
                    c.ArcTo(new Point(rt + rb, h), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, false);   // bottom-left
                    c.LineTo(new Point(w + rt - rb, h), true, false);
                    c.ArcTo(new Point(w + rt, h - rb), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, false); // bottom-right
                    c.LineTo(new Point(w + rt, rt), true, false);
                    c.ArcTo(new Point(w + 2 * rt, 0), new Size(rt, rt), 0, false, SweepDirection.Clockwise, true, false);       // top-right flare
                }
            }
            g.Freeze();
            return g;
        }
    }
}
