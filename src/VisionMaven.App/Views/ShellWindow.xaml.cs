using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VisionMaven.App.ViewModels;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;
using VisionMaven.Core.Shell;
using Application = System.Windows.Application;
using IDialogService = VisionMaven.Core.Shell.IDialogService;
using WindowState = System.Windows.WindowState;

namespace VisionMaven.App.Views;

/// <summary>Shell 窗口：仅承载布局与窗口手势，业务逻辑全部位于 <see cref="ShellWindowViewModel"/>。</summary>
public partial class ShellWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    private readonly IUserContext _user;
    private readonly IAuthenticationService _authentication;
    private readonly IDialogService _dialogs;
    private ShellWindowViewModel? _viewModel;

    public ShellWindow(IUserContext user, IAuthenticationService authentication, IDialogService dialogs)
    {
        _user = user;
        _authentication = authentication;
        _dialogs = dialogs;

        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.WindowActionRequested -= OnWindowActionRequested;
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as ShellWindowViewModel;

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.WindowActionRequested += OnWindowActionRequested;
        _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel?.Start();
        ApplyNavigationWidth();
        ApplyLogHeight();

        _authentication.Touch();
        _dialogs.ShowInfo(
            _user.Current is null
                ? "未登录"
                : $"当前用户：{_user.Current.DisplayName}（{_user.Current.RoleName}）",
            "登录成功");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellWindowViewModel.IsNavCollapsed):
                ApplyNavigationWidth();
                break;

            case nameof(ShellWindowViewModel.IsLogCollapsed):
                ApplyLogHeight();
                break;

            default:
                break;
        }
    }

    private void ApplyNavigationWidth()
    {
        var collapsed = _viewModel?.IsNavCollapsed == true;
        NavColumn.Width = new GridLength(collapsed ? 48 : 200);
    }

    private void ApplyLogHeight()
    {
        var collapsed = _viewModel?.IsLogCollapsed == true;
        LogRow.Height = new GridLength(collapsed ? 28 : 140);
    }

    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        if (LogList.Items.Count == 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void OnWindowActionRequested(object? sender, WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Minimize:
                SystemCommands.MinimizeWindow(this);
                break;

            case WindowAction.Maximize:
                if (WindowState == WindowState.Maximized)
                {
                    SystemCommands.RestoreWindow(this);
                }
                else
                {
                    SystemCommands.MaximizeWindow(this);
                }

                break;

            case WindowAction.Close:
                Close();
                break;

            default:
                break;
        }
    }

    /// <summary>最大化时按当前显示器工作区裁剪，避免覆盖任务栏。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.Monitor;

        info.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        info.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        info.MaxSize.X = Math.Abs(workArea.Right - workArea.Left);
        info.MaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);

        Marshal.StructureToPtr(info, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;

        public NativePoint MaxSize;

        public NativePoint MaxPosition;

        public NativePoint MinTrackSize;

        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;

        public NativeRect Monitor;

        public NativeRect WorkArea;

        public uint Flags;
    }
}
