using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace LinkView.Windows.Controls;

/// <summary>轻量实时折线图。只绘制真实采样值，不做平滑或外推。</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAreaProperty = DependencyProperty.Register(
        nameof(ShowArea), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _notify;
    private static readonly Pen?[] PenCache = new Pen?[1];

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public bool ShowArea
    {
        get => (bool)GetValue(ShowAreaProperty);
        set => SetValue(ShowAreaProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Sparkline)d;
        if (control._notify is not null)
        {
            control._notify.CollectionChanged -= control.OnCollectionChanged;
        }

        control._notify = e.NewValue as INotifyCollectionChanged;
        if (control._notify is not null)
        {
            control._notify.CollectionChanged += control.OnCollectionChanged;
        }

        control.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 44 : availableSize.Height;
        return new Size(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, Math.Min(height, 120));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var values = Values?.Cast<object>()
            .Select(v => v is double d ? d : v is int i ? i : 0)
            .ToArray() ?? [];
        if (values.Length < 2)
        {
            return;
        }

        var max = MaxValue;
        if (double.IsNaN(max) || max <= 0)
        {
            max = Math.Max(1, values.Max());
        }

        max = Math.Max(max, 1);
        var stroke = Stroke;
        var pen = new Pen(stroke, 1.5) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var step = width / (values.Length - 1);
            var startX = 0.0;
            var startY = height - Math.Clamp(values[0] / max, 0, 1) * (height - 3) - 1.5;
            ctx.BeginFigure(new Point(startX, startY), isFilled: false, isClosed: false);
            for (var i = 1; i < values.Length; i++)
            {
                var x = i * step;
                var y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 3) - 1.5;
                ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        if (ShowArea)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                var step = width / (values.Length - 1);
                ctx.BeginFigure(new Point(0, height), isFilled: true, isClosed: true);
                for (var i = 0; i < values.Length; i++)
                {
                    var x = i * step;
                    var y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 3) - 1.5;
                    ctx.LineTo(new Point(x, y), isStroked: false, isSmoothJoin: false);
                }

                ctx.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
            }

            area.Freeze();
            var color = (stroke as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            var areaBrush = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B));
            areaBrush.Freeze();
            dc.DrawGeometry(areaBrush, null, area);
        }
    }
}
