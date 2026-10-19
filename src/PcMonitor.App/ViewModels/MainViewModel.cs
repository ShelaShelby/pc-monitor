using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using PcMonitor.Core;

namespace PcMonitor.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    public const string PawnIoUrl = "https://pawnio.eu";

    private readonly ISensorProvider _provider;
    private readonly AppSettings _settings;
    private readonly MetricTracker _tracker = new(60);
    private readonly SemaphoreSlim _providerGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _virtualTime;
    private int _ticks;
    private bool _specsLoaded;
    private bool _showSpecs;
    private double _cardWidth = 440;
    private string _status = "Starting…";
    private DriverStatus _pawnIo;
    private bool _rechecking;
    /// <summary>The provider call running on the thread pool, if any (shutdown waits for it).</summary>
    private Task? _providerCall;

    /// <param name="virtualTime">Screenshot mode: each tick advances time by one interval without waiting.</param>
    public MainViewModel(ISensorProvider provider, AppSettings settings, bool virtualTime = false)
    {
        _provider = provider;
        _settings = settings;
        _virtualTime = virtualTime;
        _pawnIo = provider.PawnIo;
        foreach (PartKind kind in Enum.GetValues<PartKind>())
            Cards.Add(new CardViewModel(kind));
        OpenPawnIoCommand = new RelayCommand(OpenPawnIo);
        RecheckCommand = new RelayCommand(() => _ = RecheckAsync(), () => !_rechecking);
        ResetMinMaxCommand = new RelayCommand(() => _tracker.ResetMinMax());
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Test hook (--interval-ms): update interval that bypasses the 0.5 / 1 / 2 s choice; not saved.</summary>
    public TimeSpan? IntervalOverride { get; init; }

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    /// <summary>"0.5": major.minor of the assembly version (Directory.Build.props).</summary>
    public static string VersionText { get; } = typeof(MainViewModel).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "";

    public string VersionLabel => $"v{VersionText}";

    public RelayCommand OpenPawnIoCommand { get; }
    public RelayCommand RecheckCommand { get; }
    public RelayCommand ResetMinMaxCommand { get; }

    public event EventHandler<int>? Ticked;

    public int Ticks => _ticks;

    public string ProviderName => _provider.Name;

    public string Status { get => _status; private set => Set(ref _status, value); }

    public double CardWidth { get => _cardWidth; set => Set(ref _cardWidth, value); }

    public bool ShowSpecs { get => _showSpecs; set { if (Set(ref _showSpecs, value)) OnPropertyChanged(nameof(SpecsVisible)); } }

    /// <summary>Specs are shown when "Show specs" is on and Compact is off.</summary>
    public bool SpecsVisible => _showSpecs && !Compact;

    public IReadOnlyList<LayoutOption> LayoutOptions => LayoutFit.Options;

    public LayoutMode Layout
    {
        get => _settings.Layout;
        set
        {
            if (value == _settings.Layout || !Enum.IsDefined(value))
                return;
            _settings.Layout = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public bool Compact
    {
        get => _settings.Compact;
        set
        {
            if (value == _settings.Compact)
                return;
            _settings.Compact = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpecsVisible));
            OnPropertyChanged(nameof(CardMargin));
            OnPropertyChanged(nameof(CardPadding));
            OnPropertyChanged(nameof(HeroMargin));
            OnPropertyChanged(nameof(RowMargin));
            OnPropertyChanged(nameof(SparkHeight));
            SettingsStore.Save(_settings);
        }
    }

    public double CardMarginSize => Compact ? CardMetrics.CompactCardMargin : CardMetrics.CardMargin;
    public System.Windows.Thickness CardMargin => new(CardMarginSize);
    public System.Windows.Thickness CardPadding => Compact
        ? new(CardMetrics.CompactCardPaddingX, 8, CardMetrics.CompactCardPaddingX, 8)
        : new(CardMetrics.CardPaddingX, 14, CardMetrics.CardPaddingX, 14);
    public System.Windows.Thickness HeroMargin => Compact ? new(0, 2, 0, 0) : new(0, 8, 0, 4);
    public System.Windows.Thickness RowMargin => Compact ? new(0, 0, 0, 0) : new(0, 2, 0, 2);
    public double SparkHeight => Compact ? 40 : 56;

    private int _gridColumns = 3;
    private double _columnWidth = CardMetrics.MinColumnWidth;

    /// <summary>Columns of the packed (masonry) panel in the scaled layouts.</summary>
    public int GridColumns { get => _gridColumns; set => Set(ref _gridColumns, value); }

    /// <summary>Logical width of one packed column, card margins included.</summary>
    public double ColumnWidth { get => _columnWidth; set => Set(ref _columnWidth, value); }

    public double[] Intervals => AppSettings.AllowedIntervals;

    public double UpdateInterval
    {
        get => _settings.UpdateIntervalSeconds;
        set
        {
            double v = AppSettings.NormalizeInterval(value);
            if (v == _settings.UpdateIntervalSeconds)
                return;
            _settings.UpdateIntervalSeconds = v;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public bool IsFahrenheit
    {
        get => _settings.TemperatureUnit == TemperatureUnit.Fahrenheit;
        set
        {
            TemperatureUnit unit = value ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius;
            if (unit == _settings.TemperatureUnit)
                return;
            _settings.TemperatureUnit = unit;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
            Refresh();
        }
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (value == _settings.AlwaysOnTop)
                return;
            _settings.AlwaysOnTop = value;
            OnPropertyChanged();
            SettingsStore.Save(_settings);
        }
    }

    public bool PawnIoBannerVisible => _pawnIo.State is DriverState.Missing or DriverState.InstalledNotLoaded;

    public string PawnIoDetail => _pawnIo.Detail;

    private SystemSnapshot? _last;

    public async Task RunAsync()
    {
        CancellationToken ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan started = _clock.Elapsed;
            SystemSnapshot? snapshot = null;
            await _providerGate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                snapshot = await Track(Task.Run(_provider.Read, ct)).ConfigureAwait(true);
                if (!_specsLoaded)
                {
                    IReadOnlyList<PartSpecs> specs = await Track(Task.Run(_provider.GetSpecs, ct)).ConfigureAwait(true);
                    ApplySpecs(specs);
                    _specsLoaded = true;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Write($"Read failed: {ex}");
                Status = $"Sensor read failed: {ex.Message}";
            }
            finally
            {
                _providerGate.Release();
            }

            if (snapshot is not null && !ct.IsCancellationRequested)
            {
                // A failure while showing one update must not end this loop (the values would freeze).
                try
                {
                    snapshot = SnapshotGuard.Sanitize(snapshot);
                    double t = _virtualTime ? _ticks * _settings.UpdateIntervalSeconds : _clock.Elapsed.TotalSeconds;
                    _tracker.Record(snapshot, t);
                    _last = snapshot;
                    Refresh();
                    _ticks++;
                    double ms = (_clock.Elapsed - started).TotalMilliseconds;
                    Status = string.Create(CultureInfo.InvariantCulture,
                        $"{_provider.Name} · every {_settings.UpdateIntervalSeconds:0.#} s · read {ms:0} ms · {DateTime.Now:HH:mm:ss}");
                    Ticked?.Invoke(this, _ticks);
                }
                catch (Exception ex)
                {
                    Log.Write($"Update failed: {ex}");
                    Status = $"Update failed: {ex.Message}";
                }
            }

            TimeSpan wait = _virtualTime
                ? TimeSpan.FromMilliseconds(15)
                : (IntervalOverride ?? TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds)) - (_clock.Elapsed - started);
            if (wait < TimeSpan.FromMilliseconds(15))
                wait = TimeSpan.FromMilliseconds(15);
            try
            {
                await Task.Delay(wait, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Refresh()
    {
        if (_last is null)
            return;
        foreach (CardViewModel card in Cards)
        {
            // One card failing must not stop the others from updating.
            try
            {
                card.Update(_last.Find(card.Kind), _tracker, _settings.TemperatureUnit);
            }
            catch (Exception ex)
            {
                Log.Write($"Card {card.Kind} update failed: {ex}");
            }
        }
    }

    private Task<T> Track<T>(Task<T> call)
    {
        _providerCall = call;
        return call;
    }

    /// <summary>
    /// Waits until no provider call is running on the thread pool, so the provider can be disposed
    /// without closing the driver under a running read. False if a call is still running after the timeout.
    /// </summary>
    public bool WaitForProvider(TimeSpan timeout)
    {
        Task? call = _providerCall;
        if (call is null || call.IsCompleted)
            return true;
        try
        {
            return call.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;   // the call finished (with an error the loop already logs)
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _cts.IsCancellationRequested)
            return;
        // Raised on a SystemEvents thread; drivers can come back reset after sleep, so reopen hardware access.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _ = ReopenAsync()));
    }

    private async Task ReopenAsync()
    {
        if (_cts.IsCancellationRequested)
            return;
        Status = "Resumed from sleep: reopening sensors…";
        try
        {
            await _providerGate.WaitAsync(_cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            await Track(Task.Run(() => { _provider.Reopen(); return true; })).ConfigureAwait(true);
            _specsLoaded = false;
        }
        catch (Exception ex)
        {
            Log.Write($"Reopen after resume failed: {ex}");
        }
        finally
        {
            _providerGate.Release();
        }
    }

    private void ApplySpecs(IReadOnlyList<PartSpecs> specs)
    {
        foreach (CardViewModel card in Cards)
            card.Specs = specs.FirstOrDefault(s => s.Kind == card.Kind)?.Lines ?? Array.Empty<SpecLine>();
    }

    private static void OpenPawnIo()
    {
        try
        {
            // The only network action in the app: hand the URL to the default browser.
            Process.Start(new ProcessStartInfo(PawnIoUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Opening {PawnIoUrl} failed: {ex.Message}");
        }
    }

    private async Task RecheckAsync()
    {
        _rechecking = true;
        RecheckCommand.RaiseCanExecuteChanged();
        Status = "Rechecking PawnIO…";
        await _providerGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await Track(Task.Run(() => { _provider.Recheck(); return true; })).ConfigureAwait(true);
            _specsLoaded = false;
        }
        catch (Exception ex)
        {
            Log.Write($"Recheck failed: {ex}");
        }
        finally
        {
            _providerGate.Release();
            _pawnIo = _provider.PawnIo;
            OnPropertyChanged(nameof(PawnIoBannerVisible));
            OnPropertyChanged(nameof(PawnIoDetail));
            _rechecking = false;
            RecheckCommand.RaiseCanExecuteChanged();
            Status = $"PawnIO: {_pawnIo.Detail}";
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested)
            return;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        // Cancel only; the token stays usable for the loop's last iteration (not disposed while it may still run).
        _cts.Cancel();
    }
}
