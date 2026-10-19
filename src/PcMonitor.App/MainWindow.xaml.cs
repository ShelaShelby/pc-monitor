using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        if (options.Layout is LayoutMode layout)
            settings.Layout = layout;
        if (options.Compact)
            settings.Compact = true;
        _vm = new MainViewModel(provider, settings, virtualTime: options.Screenshot is not null && options.Fake)
        {
            ShowSpecs = options.ShowSpecs,
        };
        DataContext = _vm;
        if (options.Width is double w && options.Height is double h)
        {
            Width = w;
            Height = h;
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Scroller.SizeChanged += (_, _) => ApplyLayout();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Layout) or nameof(MainViewModel.Compact) or nameof(MainViewModel.ShowSpecs))
                ApplyLayout();
        };
        // Card heights change when sensors appear or values wrap; re-fit after each update.
        _vm.Ticked += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ApplyLayout()));
        LocationChanged += (_, _) => RememberNormalRect();
        SizeChanged += (_, _) => RememberNormalRect();
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout();
        if (_options.Screenshot is not null)
            _vm.Ticked += OnTickForScreenshot;
        _ = _vm.RunAsync();
    }

    private int _layoutPasses;

    /// <summary>
    /// Auto: as many ~430 px columns as fit, sharing the width evenly, unscaled (WrapPanel).
    /// Fit / fixed columns: a uniform grid of natural-size cards (430 px cells, height of the tallest
    /// card) scaled with a LayoutTransform; LayoutFit decides columns and scale.
    /// </summary>
    private void ApplyLayout()
    {
        double width = Scroller.ActualWidth - Scroller.Padding.Left - Scroller.Padding.Right;
        double height = Scroller.ActualHeight - Scroller.Padding.Top - Scroller.Padding.Bottom;
        if (width <= 0 || height <= 0)
            return;
        bool changed;
        if (_vm.Layout == LayoutMode.Auto)
        {
            changed = SetPanel("WrapCards", 1, ScrollBarVisibility.Auto, HorizontalAlignment.Stretch);
            double available = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : width;
            int columns = Math.Max(1, (int)Math.Floor(available / TargetCardWidth));
            double cardWidth = Math.Max(300, Math.Floor(available / columns) - 2 * _vm.CardMarginSize - 1);
            changed |= cardWidth != _vm.CardWidth;
            _vm.CardWidth = cardWidth;
        }
        else
        {
            double cellWidth = TargetCardWidth;
            double cardWidth = cellWidth - 2 * _vm.CardMarginSize;
            changed = cardWidth != _vm.CardWidth;
            _vm.CardWidth = cardWidth;
            double cellHeight = NaturalCellHeight();
            if (cellHeight <= 0)
                cellHeight = cellWidth;
            // 1 px slack so rounding never brings up a scrollbar in Fit.
            FitResult fit = LayoutFit.Plan(_vm.Layout, width - 1, height - 1, cellWidth, cellHeight, _vm.Cards.Count, SystemParameters.VerticalScrollBarWidth);
            changed |= fit.Columns != _vm.GridColumns;
            _vm.GridColumns = fit.Columns;
            // Hidden (not Disabled) keeps the height unconstrained, so cards still measure at natural height.
            changed |= SetPanel("GridCards", fit.Scale, fit.Scrolls ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden, HorizontalAlignment.Center);
        }
        // Natural sizes are read from the previous layout pass; settle with a few follow-up passes.
        if (changed && _layoutPasses < 3)
        {
            _layoutPasses++;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ApplyLayout));
        }
        else
            _layoutPasses = 0;
    }

    /// <summary>Height of the tallest card including its margin, unscaled.</summary>
    private double NaturalCellHeight()
    {
        double max = 0;
        for (int i = 0; i < CardsHost.Items.Count; i++)
            if (CardsHost.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                max = Math.Max(max, fe.DesiredSize.Height);
        return Math.Ceiling(max);
    }

    private bool SetPanel(string panelKey, double scale, ScrollBarVisibility scroll, HorizontalAlignment align)
    {
        bool changed = false;
        var panel = (ItemsPanelTemplate)FindResource(panelKey);
        if (!ReferenceEquals(CardsHost.ItemsPanel, panel))
        {
            CardsHost.ItemsPanel = panel;
            changed = true;
        }
        double current = CardsHost.LayoutTransform is ScaleTransform st ? st.ScaleX : 1;
        if (Math.Abs(current - scale) > 0.0005)
        {
            CardsHost.LayoutTransform = Math.Abs(scale - 1) < 1e-9 ? Transform.Identity : new ScaleTransform(scale, scale);
            // Ideal text formatting scales cleanly; Display mode is sharper only at 1:1.
            TextOptions.SetTextFormattingMode(CardsHost, Math.Abs(scale - 1) < 1e-9 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            changed = true;
        }
        if (Scroller.VerticalScrollBarVisibility != scroll)
        {
            Scroller.VerticalScrollBarVisibility = scroll;
            changed = true;
        }
        CardsHost.HorizontalAlignment = align;
        return changed;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.None)
        {
            _vm.Layout = LayoutFit.Next(_vm.Layout);
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
        {
            Left = 0;
            Top = 0;
            return;
        }
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
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
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
