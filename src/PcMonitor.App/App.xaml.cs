using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using PcMonitor.App.Hardware;
using PcMonitor.Core;

namespace PcMonitor.App;

public partial class App : Application
{
    internal sealed record Options(
        bool Fake,
        bool FakeNoPawnIo,
        string? Screenshot,
        int Ticks,
        double? Width,
        double? Height,
        bool ShowSpecs,
        LayoutMode? Layout,
        bool Compact,
        bool OpenLayoutBox,
        int? IntervalMs = null,
        int? CycleEvery = null);

    internal static Options ParseArgs(string[] args)
    {
        bool fake = false, noPawn = false, specs = false, compact = false, openLayout = false;
        LayoutMode? layout = null;
        string? shot = null;
        int ticks = 70;
        double? w = null, h = null;
        int? intervalMs = null, cycleEvery = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a)
            {
                case "--fake": fake = true; break;
                case "--fake-no-pawnio": fake = true; noPawn = true; break;
                case "--show-specs": specs = true; break;
                case "--compact": compact = true; break;
                case "--open-layout-box": openLayout = true; break;
                case "--layout" when next is not null:
                    // fit, auto, 2, 3, 4
                    layout = next.ToLowerInvariant() switch
                    {
                        "fit" => LayoutMode.Fit,
                        "auto" => LayoutMode.Auto,
                        "2" => LayoutMode.Columns2,
                        "3" => LayoutMode.Columns3,
                        "4" => LayoutMode.Columns4,
                        _ => null,
                    };
                    i++;
                    break;
                case "--screenshot" when next is not null: shot = next; i++; break;
                case "--ticks" when next is not null: ticks = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                case "--size" when next is not null:
                    string[] p = next.Split('x');
                    w = double.Parse(p[0], CultureInfo.InvariantCulture);
                    h = double.Parse(p[1], CultureInfo.InvariantCulture);
                    i++;
                    break;
                // Soak test hooks: fast updates, and cycle layout / units / compact every N updates.
                case "--interval-ms" when next is not null: intervalMs = Math.Max(10, int.Parse(next, CultureInfo.InvariantCulture)); i++; break;
                case "--cycle-every" when next is not null: cycleEvery = Math.Max(1, int.Parse(next, CultureInfo.InvariantCulture)); i++; break;
                case "--log" when next is not null: Log.FilePath = Path.GetFullPath(next); i++; break;
                case "--settings" when next is not null: SettingsStore.FilePath = Path.GetFullPath(next); i++; break;
            }
        }
        return new Options(fake, noPawn, shot, Math.Max(1, ticks), w, h, specs, layout, compact, openLayout, intervalMs, cycleEvery);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Options options = ParseArgs(e.Args);
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Write($"Unhandled: {ev.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Write($"Unobserved: {ev.Exception}"); ev.SetObserved(); };
        Log.Write($"Start pc-monitor {typeof(App).Assembly.GetName().Version} args='{string.Join(' ', e.Args)}'");

        // One real instance at a time: two would open the sensor drivers twice and fight over settings.json.
        if (!options.Fake && !SingleInstance.TryAcquire())
        {
            Log.Write("Another instance is running; bringing it to the front and exiting");
            SingleInstance.ActivateOther();
            Shutdown(0);
            return;
        }

        ISensorProvider provider = options.Fake
            ? new FakeSensorProvider(pawnIoInstalled: !options.FakeNoPawnIo)
            : new LhmSensorProvider();
        try
        {
            provider.Open();
        }
        catch (Exception ex)
        {
            Log.Write($"Provider open failed: {ex}");
            if (options.Screenshot is null)
                MessageBox.Show($"Could not start hardware monitoring:\n{ex.Message}\n\nLog: {Log.FilePath}", "pc-monitor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        AppSettings settings = SettingsStore.Load();
        var window = new MainWindow(provider, settings, options);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write($"UI exception: {e.Exception}");
        e.Handled = true;
    }
}
