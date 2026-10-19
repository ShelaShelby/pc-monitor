using System.Collections.ObjectModel;
using PcMonitor.Core;

namespace PcMonitor.App.ViewModels;

public sealed class CardViewModel : ObservableObject
{
    private string _subtitle = "";
    private string? _notice;
    private IReadOnlyList<SpecLine> _specs = Array.Empty<SpecLine>();

    public CardViewModel(PartKind kind)
    {
        Kind = kind;
        Layout = CardLayout.For(kind);
        Title = CardLayout.TitleOf(kind);
        Hero = new MetricViewModel(Layout.HeroKey);
        SecondHero = new MetricViewModel(Layout.SecondHeroKey ?? "");
    }

    public PartKind Kind { get; }
    public CardLayout Layout { get; }
    public string Title { get; }

    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    public string? Notice { get => _notice; private set { if (Set(ref _notice, value)) OnPropertyChanged(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(_notice);

    public MetricViewModel Hero { get; }
    public MetricViewModel SecondHero { get; }
    public bool HasSecondHero => Layout.SecondHeroKey is not null;

    /// <summary>All metrics except the two hero ones.</summary>
    public ObservableCollection<MetricViewModel> Rows { get; } = new();

    public IReadOnlyList<SpecLine> Specs { get => _specs; set { if (Set(ref _specs, value)) OnPropertyChanged(nameof(HasSpecs)); } }
    public bool HasSpecs => _specs.Count > 0;

    public void Update(PartSnapshot? part, MetricTracker tracker, TemperatureUnit unit)
    {
        if (part is null)
        {
            Notice = "No data";
            return;
        }
        Subtitle = part.Subtitle;
        Notice = part.Notice;

        UpdateHero(Hero, part, Layout.HeroKey, tracker, unit, Palette.Accent);
        if (Layout.SecondHeroKey is { } second)
            UpdateHero(SecondHero, part, second, tracker, unit, Palette.Accent2);

        var rowMetrics = part.Metrics.Where(m => m.Key != Layout.HeroKey && m.Key != Layout.SecondHeroKey).ToList();
        if (Rows.Count != rowMetrics.Count || !Rows.Select(r => r.Key).SequenceEqual(rowMetrics.Select(m => m.Key)))
        {
            Rows.Clear();
            foreach (Metric m in rowMetrics)
                Rows.Add(new MetricViewModel(m.Key));
        }
        for (int i = 0; i < rowMetrics.Count; i++)
        {
            tracker.TryGet(Kind, rowMetrics[i].Key, out MetricHistory? h);
            Rows[i].Update(rowMetrics[i], h, unit, withHistory: false, Palette.Text);
        }
    }

    private void UpdateHero(MetricViewModel vm, PartSnapshot part, string key, MetricTracker tracker, TemperatureUnit unit, System.Windows.Media.Brush brush)
    {
        Metric metric = part.Find(key) ?? new Metric(key, key, MetricUnit.Percent, Reading.Unavailable("not reported"));
        tracker.TryGet(Kind, key, out MetricHistory? h);
        vm.Update(metric, h, unit, withHistory: true, brush);
    }
}
