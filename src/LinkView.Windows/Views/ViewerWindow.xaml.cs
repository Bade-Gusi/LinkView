using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LinkView.Windows.ViewModels;

namespace LinkView.Windows.Views;

public partial class ViewerWindow : Window
{
    private readonly ScaleTransform _scale = new(1, 1);
    private double _zoom = 1;

    public ViewerWindow(ShareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        VideoImage.RenderTransform = _scale;
        VideoImage.RenderTransformOrigin = new Point(0.5, 0.5);
        KeyDown += OnKeyDown;
        MouseMove += (_, e) =>
        {
            Overlay.Opacity = e.GetPosition(this).Y < 80 ? 1 : 0.15;
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Close();
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 6);
        _scale.ScaleX = _zoom;
        _scale.ScaleY = _zoom;
        e.Handled = true;
    }

    private void FitClick(object sender, RoutedEventArgs e)
    {
        VideoImage.Stretch = Stretch.Uniform;
        ResetZoom();
    }

    private void FillClick(object sender, RoutedEventArgs e)
    {
        VideoImage.Stretch = Stretch.UniformToFill;
        ResetZoom();
    }

    private void OriginalClick(object sender, RoutedEventArgs e)
    {
        VideoImage.Stretch = Stretch.None;
        ResetZoom();
    }

    private void ResetZoom()
    {
        _zoom = 1;
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
