using System.Windows.Media;
using PcMonitor.Core;

namespace PcMonitor.App.ViewModels;

/// <summary>One metric: value, min/max since start, colour, and (for hero metrics) its 60 s history.</summary>
public sealed class MetricViewModel : ObservableObject
{
    private string _label = "";
    private string _value = Formatter.NotAvailable;
    private string _unit = "";
    private MetricUnit _valueUnit = MetricUnit.Count;
    private string _minMax = "";
    private string? _reason;
    private Brush _brush = Palette.Text;
    private IReadOnlyList<HistoryPoint> _history = Array.Empty<HistoryPoint>();
    private double _rangeMin = double.NaN;
    private double _rangeMax = double.NaN;

    public MetricViewModel(string key) => Key = key;

    public string Key { get; }

    public string Label { get => _label; private set => Set(ref _label, value); }
    public string Value { get => _value; private set => Set(ref _value, value); }
    public string Unit { get => _unit; private set => Set(ref _unit, value); }

    /// <summary>Unit of the raw values (for the sparkline range).</summary>
    public MetricUnit ValueUnit { get => _valueUnit; private set => Set(ref _valueUnit, value); }
    public string MinMax { get => _minMax; private set => Set(ref _minMax, value); }

    /// <summary>Why the value is n/a; null when there is a value.</summary>
    public string? Reason { get => _reason; private set { if (Set(ref _reason, value)) OnPropertyChanged(nameof(HasReason)); } }

    public bool HasReason => _reason is not null;

    public Brush Brush { get => _brush; private set => Set(ref _brush, value); }

    /// <summary>Sparkline stroke: threshold colour for temperatures, accent otherwise.</summary>
    public Brush SparkBrush { get; private set; } = Palette.Accent;

    public IReadOnlyList<HistoryPoint> History { get => _history; private set => Set(ref _history, value); }
    public double RangeMin { get => _rangeMin; private set => Set(ref _rangeMin, value); }
    public double RangeMax { get => _rangeMax; private set => Set(ref _rangeMax, value); }

    public void Update(Metric metric, MetricHistory? history, TemperatureUnit tempUnit, bool withHistory, Brush defaultBrush)
    {
        Label = metric.Label;
        FormattedValue f = Formatter.Format(metric, tempUnit);
        Value = f.Value;
        Unit = f.Unit;
        ValueUnit = metric.Unit;
        Reason = metric.Unit != MetricUnit.Text && !metric.Reading.HasValue ? metric.Reading.Reason : null;
        MinMax = history is null || metric.Unit == MetricUnit.Text
            ? ""
            : Formatter.KeepMinMaxHalvesTogether(Formatter.FormatMinMax(metric.Unit, history.MinMax.Min, history.MinMax.Max, tempUnit));

        Brush brush = defaultBrush;
        if (metric.Unit == MetricUnit.Celsius)
            brush = Palette.For(ThresholdTable.For(metric).Evaluate(metric.Reading));
        else if (!metric.Reading.HasValue && metric.Unit != MetricUnit.Text)
            brush = Palette.Unknown;
        Brush = brush;

        if (withHistory && history is not null)
        {
            SparkBrush = metric.Unit == MetricUnit.Celsius ? (metric.Reading.HasValue ? brush : Palette.Unknown) : defaultBrush;
            OnPropertyChanged(nameof(SparkBrush));
            if (metric.Unit == MetricUnit.Percent)
            {
                RangeMin = 0;
                RangeMax = 100;
            }
            History = history.History.Snapshot();
        }
    }
}
