using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcMonitor.App.ViewModels;
using PcMonitor.Core;

namespace PcMonitor.App;

public partial class MainWindow : Window
{
    private const double TargetCardWidth = 430;

    private readonly MainViewModel _vm;
    private readonly ISensorProvider _provider;
    private readonly AppSettings _settings;
    private readonly App.Options _options;
    private NativeMethods.RECT? _normalRect;
    private bool _fullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    internal MainWindow(ISensorProvider provider, AppSettings settings, App.Options options)
    {
        InitializeComponent();
        _provider = provider;
        _settings = settings;
        _options = options;
        _vm = new MainViewModel(provider, settings, virtualTime: options.Screenshot is not null && options.Fake)
        {
            ShowSpecs = options.ShowSpecs,
        };
        DataContext = _vm;
        if (options.Width is double w && options.Height is double h)
        {
            Width = w;
            Height = h;
            if (options.Screenshot is not null)
            {
                // CI desktops are small (1024x768) and Windows caps the window to the screen.
                // Lay the content out at the requested size so the capture shows the real layout.
                Root.Width = w;
                Root.Height = h;
            }
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Scroller.SizeChanged += (_, _) => ReflowCards();
        LocationChanged += (_, _) => RememberNormalRect();
        SizeChanged += (_, _) => RememberNormalRect();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ReflowCards();
        if (_options.Screenshot is not null)
            _vm.Ticked += OnTickForScreenshot;
        _ = _vm.RunAsync();
    }

    /// <summary>Cards reflow to the window: as many ~430 px columns as fit, sharing the width evenly.</summary>
    private void ReflowCards()
    {
        double available = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth - 16;
        if (available <= 0)
            return;
        int columns = Math.Max(1, (int)Math.Floor(available / TargetCardWidth));
        _vm.CardWidth = Math.Max(300, Math.Floor(available / columns) - 16 - 1);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _stateBeforeFullscreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            _fullscreen = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // Normal first, so Maximized with no chrome covers the taskbar on the current monitor.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullscreen = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
        }
    }

    // ---------- window placement (physical pixels, so it survives mixed-DPI monitor setups) ----------

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void RememberNormalRect()
    {
        if (WindowState == WindowState.Normal && !_fullscreen && Handle != IntPtr.Zero && NativeMethods.GetWindowRect(Handle, out NativeMethods.RECT r))
            _normalRect = r;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_options.Screenshot is not null)
            return; // deterministic size/position for CI screenshots
        ScreenRect? restored = WindowPlacement.Restore(_settings.Window, NativeMethods.MonitorWorkAreas());
        if (restored is { } r)
        {
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, (int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            RememberNormalRect();
            if (_settings.Window!.Maximized)
                WindowState = WindowState.Maximized;
            if (_settings.Window.Fullscreen)
                Dispatcher.BeginInvoke(ToggleFullscreen);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = (SystemParameters.WorkArea.Height - Height) / 2;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _vm.Dispose();
        if (_options.Screenshot is null)
        {
            RememberNormalRect();
            if (_normalRect is { } r)
            {
                _settings.Window = new WindowBounds
                {
                    Left = r.Left,
                    Top = r.Top,
                    Width = r.Right - r.Left,
                    Height = r.Bottom - r.Top,
                    Maximized = _fullscreen ? _stateBeforeFullscreen == WindowState.Maximized : WindowState == WindowState.Maximized,
                    Fullscreen = _fullscreen,
                    Monitor = NativeMethods.MonitorName(Handle),
                };
            }
            SettingsStore.Save(_settings);
        }
        try
        {
            _provider.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write($"Provider dispose: {ex.Message}");
        }
    }

    // ---------- screenshot mode (CI) ----------

    private void OnTickForScreenshot(object? sender, int ticks)
    {
        if (ticks < _options.Ticks)
            return;
        _vm.Ticked -= OnTickForScreenshot;
        // Let layout and rendering catch up with the last update before capturing.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                SaveScreenshot(_options.Screenshot!);
                Log.Write($"Screenshot written: {_options.Screenshot}");
            }
            catch (Exception ex)
            {
                Log.Write($"Screenshot failed: {ex}");
            }
            Close();
        }));
    }

    private void SaveScreenshot(string path)
    {
        FrameworkElement target = Root;
        target.UpdateLayout();
        int width = (int)Math.Ceiling(target.ActualWidth);
        int height = (int)Math.Ceiling(target.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }
}
