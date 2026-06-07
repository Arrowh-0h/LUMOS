using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lumos.Desktop.Views;

/// <summary>
/// Round countdown indicator for TOTP. Two dependency properties drive it:
///   - Fraction (0..1, where 1 = full ring, 0 = empty)
///   - SecondsRemaining (shown centered as an integer)
/// XAML binds these to EntryDetailViewModel.TotpFractionRemaining /
/// .TotpSecondsRemaining; we recompute the arc geometry each time Fraction
/// changes so the depleting ring is always crisp.
/// </summary>
public partial class TotpRing : UserControl
{
    public TotpRing()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(TotpRing),
        new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty SecondsRemainingProperty = DependencyProperty.Register(
        nameof(SecondsRemaining), typeof(double), typeof(TotpRing),
        new PropertyMetadata(30.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsExpiringSoonProperty = DependencyProperty.Register(
        nameof(IsExpiringSoon), typeof(bool), typeof(TotpRing),
        new PropertyMetadata(false, OnExpiringChanged));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double SecondsRemaining
    {
        get => (double)GetValue(SecondsRemainingProperty);
        set => SetValue(SecondsRemainingProperty, value);
    }

    public bool IsExpiringSoon
    {
        get => (bool)GetValue(IsExpiringSoonProperty);
        set => SetValue(IsExpiringSoonProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TotpRing r) r.Redraw();
    }

    private static void OnExpiringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TotpRing r && r.IsLoaded) r.UpdateColor();
    }

    private void Redraw()
    {
        if (!IsLoaded) return;

        // Ring lives in the inner area after the 6px margin. Outer 84-12=72,
        // halve for radius = 36. The stroke is 3 px so the centerline is at
        // r = 36 - 1.5; close enough to 36 for our purposes.
        const double size = 72;
        const double radius = size / 2;
        var center = new Point(size / 2, size / 2);

        var fraction = Math.Clamp(Fraction, 0, 1);
        SecondsLabel.Text = ((int)Math.Ceiling(SecondsRemaining)).ToString();

        // Arc goes clockwise starting at 12 o'clock. When Fraction == 1 we
        // draw a full circle — but WPF arc geometry can't draw a 360° arc
        // as a single segment (start and end points coincide). For Fraction
        // >= 0.999 we use a two-arc split.
        if (fraction <= 0.001)
        {
            ArcPath.Data = null;
            UpdateColor();
            return;
        }

        var endAngle = fraction * 360.0;
        var startPt = new Point(center.X, center.Y - radius); // 12 o'clock
        var endPt = PointOnCircle(center, radius, endAngle);

        var figure = new PathFigure { StartPoint = startPt, IsClosed = false };
        if (fraction >= 0.999)
        {
            // Draw two halves so the arc is well-defined.
            var midPt = PointOnCircle(center, radius, 180);
            figure.Segments.Add(new ArcSegment(midPt, new Size(radius, radius), 0,
                isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
            figure.Segments.Add(new ArcSegment(startPt, new Size(radius, radius), 0,
                isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
        }
        else
        {
            figure.Segments.Add(new ArcSegment(endPt, new Size(radius, radius), 0,
                isLargeArc: endAngle > 180, SweepDirection.Clockwise, isStroked: true));
        }
        var geom = new PathGeometry();
        geom.Figures.Add(figure);
        ArcPath.Data = geom;
        UpdateColor();
    }

    private void UpdateColor()
    {
        // < 5 seconds: ring + label go danger red. Otherwise gold.
        var brushKey = IsExpiringSoon ? "DangerBrush" : "AccentGoldBrush";
        if (TryFindResource(brushKey) is Brush b)
        {
            ArcPath.Stroke = b;
            SecondsLabel.Foreground = b;
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        // 0° = 12 o'clock (straight up), increasing clockwise.
        var radians = (angleDegrees - 90) * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
