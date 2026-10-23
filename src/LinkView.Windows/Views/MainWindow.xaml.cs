using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using LinkView.Core.Settings;
using LinkView.Windows.ViewModels;

namespace LinkView.Windows.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ViewerRequested += OnViewerRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Theme.ThemeApplied += (_, _) => ApplyDwmTheme();
        SourceInitialized += (_, _) => ApplyDwmTheme();
        StateChanged += (_, _) => UpdateMaximizeIcon();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            viewModel.ViewerRequested -= OnViewerRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Dispose();
        };
    }

    private void OnViewerRequested(object? sender, ShareViewModel vm)
    {
        var viewer = new ViewerWindow(vm) { Owner = this };
        viewer.Show();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            AnimatePage();
        }
    }

    private void AnimatePage()
    {
        var level = _viewModel.Settings.Draft.Animations;
        if (level == AnimationLevel.None)
        {
            PageHost.Opacity = 1;
            PageTranslate.Y = 0;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(level == AnimationLevel.Reduced ? 100 : 190);
        PageHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.2, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        PageTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void UpdateMaximizeIcon()
    {
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (_viewModel.IsSharing || _viewModel.IsSessionActive)
        {
            var result = MessageBox.Show(
                "当前有正在进行的会话。退出 LinkView 会立即停止屏幕共享并断开连接。\n\n确定要退出吗？",
                "退出 LinkView", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _allowClose = true;
    }

    private void ApplyDwmTheme()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dark = _viewModel.Theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
