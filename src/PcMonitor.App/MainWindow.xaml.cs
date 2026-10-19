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
    private const double TargetCardWidth = CardMetrics.MinColumnWidth;

    private readonly MainViewModel _vm;
    private readonly ISensorProvider _provider;
    private readonly AppSettings _settings;
    private readonly App.Options _options;
    private NativeMethods.RECT? _normalRect;
    private bool _fullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    /// <summary>Last state other than Minimized, so closing from the taskbar keeps "maximized".</summary>
    private WindowState _restoreState = WindowState.Normal;

    internal MainWindow(ISensorProvider provider, AppSettings settings, App.Options options)
    {
        InitializeComponent();
        Title = $"PC Monitor {MainViewModel.VersionText}";
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
            IntervalOverride = options.IntervalMs is int ms ? TimeSpan.FromMilliseconds(ms) : null,
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
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                _restoreState = WindowState;
        };
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLayout();
        if (_options.Screenshot is not null)
            _vm.Ticked += OnTickForScreenshot;
        if (_options.CycleEvery is int every)
            _vm.Ticked += (_, ticks) => CycleForSoak(ticks, every);
        _ = _vm.RunAsync();
    }

    private int _layoutPasses;

    /// <summary>
    /// Auto: as many ~430 px columns as fit, sharing the width evenly, unscaled (WrapPanel).
    /// Fit / fixed columns: cards packed into columns by their measured heights (MasonryPanel), scaled
    /// with a LayoutTransform; LayoutFit decides columns, scale and the stretched column width.
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
            double[] heights = NaturalHeights();
            if (heights.Length == 0 || heights.Any(h => h <= 0))
                heights = Enumerable.Repeat(TargetCardWidth, Math.Max(1, _vm.Cards.Count)).ToArray();
            // 1 px slack so rounding never brings up a scrollbar in Fit.
            FitResult fit = LayoutFit.Plan(_vm.Layout, width - 1, height - 1, heights, TargetCardWidth, SystemParameters.VerticalScrollBarWidth);
            // Whole pixels, rounded down, so the stretched columns never exceed the window width.
            double columnWidth = Math.Max(TargetCardWidth, Math.Floor(fit.ColumnWidth));
            double cardWidth = columnWidth - 2 * _vm.CardMarginSize;
            changed = cardWidth != _vm.CardWidth || columnWidth != _vm.ColumnWidth || fit.Columns != _vm.GridColumns;
            _vm.CardWidth = cardWidth;
            _vm.ColumnWidth = columnWidth;
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

    /// <summary>Unscaled height of each card including its margin, in card order.</summary>
    private double[] NaturalHeights()
    {
        var heights = new double[CardsHost.Items.Count];
        for (int i = 0; i < heights.Length; i++)
            if (CardsHost.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                heights[i] = Math.Ceiling(fe.DesiredSize.Height);
        return heights;
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
        NativeMethods.UseDarkTitleBar(Handle);
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
                    Maximized = (_fullscreen ? _stateBeforeFullscreen : _restoreState) == WindowState.Maximized,
                    Fullscreen = _fullscreen,
                    Monitor = NativeMethods.MonitorName(Handle),
                };
            }
            SettingsStore.Save(_settings);
        }
        // A read may still be running on the thread pool; closing the driver under it can crash the process.
        if (!_vm.WaitForProvider(TimeSpan.FromSeconds(5)))
        {
            Log.Write("Provider still busy at exit; not disposing it (the process exit releases it)");
            return;
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

    // ---------- soak test (CI) ----------

    /// <summary>Exercises the layout, unit, compact and min/max paths repeatedly so leaks show up in the soak test.</summary>
    private void CycleForSoak(int ticks, int every)
    {
        if (ticks % every != 0)
            return;
        int step = ticks / every;
        _vm.Layout = LayoutFit.Next(_vm.Layout);
        if (step % 2 == 0)
            _vm.IsFahrenheit = !_vm.IsFahrenheit;
        if (step % 3 == 0)
            _vm.Compact = !_vm.Compact;
        if (step % 4 == 0)
            _vm.ShowSpecs = !_vm.ShowSpecs;
        if (step % 5 == 0)
            _vm.ResetMinMaxCommand.Execute(null);
    }

    // ---------- screenshot mode (CI) ----------

    private void OnTickForScreenshot(object? sender, int ticks)
    {
        if (ticks < _options.Ticks)
            return;
        _vm.Ticked -= OnTickForScreenshot;
        if (_options.OpenLayoutBox)
            LayoutBox.IsDropDownOpen = true;
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
        // An open dropdown lives in its own popup window; draw it over the capture where it appears.
        if (LayoutBox.IsDropDownOpen && LayoutBox.Template?.FindName("PART_Popup", LayoutBox) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement list })
        {
            list.UpdateLayout();
            Point at = LayoutBox.TranslatePoint(new Point(0, LayoutBox.ActualHeight), target);
            var overlay = new DrawingVisual();
            using (DrawingContext dc = overlay.RenderOpen())
                dc.DrawRectangle(new VisualBrush(list), null, new Rect(at, new Size(list.ActualWidth, list.ActualHeight)));
            bitmap.Render(overlay);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }
}
