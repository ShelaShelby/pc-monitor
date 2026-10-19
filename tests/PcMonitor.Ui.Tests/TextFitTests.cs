using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PcMonitor.Core;

namespace PcMonitor.Ui.Tests;

/// <summary>
/// Measures the card texts with WPF (Segoe UI, the app's font) and checks them against the column
/// widths of the card template (CardMetrics) at the narrowest column. Widths are compared to each
/// other, never to fixed pixel numbers, so the test does not depend on the machine.
/// </summary>
public class TextFitTests
{
    public static TheoryData<double, bool> Cases => new()
    {
        { 0.55, false }, { 0.68, false }, { 1.0, false },
        { 0.55, true }, { 0.68, true }, { 1.0, true },
    };

    private const string Font = "Segoe UI";

    /// <summary>
    /// Width in logical px of <paramref name="text"/> drawn at <paramref name="scale"/>. MainWindow uses Display
    /// text formatting at 1:1 and Ideal when scaled, so the same mode is used here.
    /// </summary>
    private static double Width(string text, double fontSize, FontWeight weight, double scale)
    {
        if (text.Length == 0)
            return 0;
        TextFormattingMode mode = Math.Abs(scale - 1) < 1e-9 ? TextFormattingMode.Display : TextFormattingMode.Ideal;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(Font), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize * scale, Brushes.Black, null, mode, 1.0);
        return formatted.WidthIncludingTrailingWhitespace / scale;
    }

    /// <summary>A wide but realistic reading per unit (digits are equally wide in Segoe UI).</summary>
    private static double? Wide(MetricUnit unit) => unit switch
    {
        MetricUnit.Percent => 100,
        MetricUnit.Celsius => 100,
        MetricUnit.MegaHertz => 8888,
        MetricUnit.Watt => 888,
        MetricUnit.Rpm => 8888,
        MetricUnit.Bytes => 1023.0 * 1024 * 1024 * 1024,
        MetricUnit.BytesPerSecond => 1023.0 * 1024 * 1024,
        MetricUnit.Hours => 88888,
        MetricUnit.Volt => 1.888,
        MetricUnit.Count => 8888,
        _ => null,
    };

    /// <summary>Lower end in the next smaller binary unit, so min and max show different units (the wider form).</summary>
    private static double? WideLow(MetricUnit unit) => unit is MetricUnit.Bytes or MetricUnit.BytesPerSecond ? Wide(unit) / 1024 : Wide(unit);

    private static IEnumerable<FormattedValue> Values(Metric m, TemperatureUnit t)
    {
        yield return Formatter.Format(m, t);
        if (m.Unit == MetricUnit.Text)
            yield break;
        yield return Formatter.Format(m.Unit, null, t);
        yield return Formatter.Format(m.Unit, Wide(m.Unit), t);
    }

    private static IEnumerable<string> MinMaxHalves(Metric m, TemperatureUnit t)
    {
        if (m.Unit == MetricUnit.Text)
            yield break;
        string[] texts =
        {
            Formatter.FormatMinMax(m.Unit, null, null, t),
            Formatter.FormatMinMax(m.Unit, m.Reading.Value, m.Reading.Value, t),
            Formatter.FormatMinMax(m.Unit, WideLow(m.Unit), Wide(m.Unit), t),
        };
        // The app only lets the line wrap between the halves (Formatter.KeepMinMaxHalvesTogether).
        foreach (string text in texts)
            foreach (string half in Formatter.KeepMinMaxHalvesTogether(text).Split(" · "))
                yield return half.Replace(' ', ' ') + " ·";
    }

    private static IEnumerable<PartSnapshot> Parts()
    {
        foreach (bool pawnIo in new[] { true, false })
        {
            var fake = new FakeSensorProvider(pawnIoInstalled: pawnIo);
            for (int i = 0; i < 5; i++)
                fake.Read();
            foreach (PartSnapshot part in fake.Read().Parts)
                yield return part;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RowTextFitsItsColumns(double scale, bool compact) => Sta.Run(() =>
    {
        // One device pixel of layout rounding at this scale.
        double rounding = 1 / scale;
        double content = CardMetrics.ContentWidth(CardMetrics.MinColumnWidth, compact);
        foreach (TemperatureUnit t in Enum.GetValues<TemperatureUnit>())
        foreach (PartSnapshot part in Parts())
        {
            CardLayout layout = CardLayout.For(part.Kind);
            List<Metric> rows = part.Metrics.Where(m => m.Key != layout.HeroKey && m.Key != layout.SecondHeroKey).ToList();
            if (rows.Count == 0)
                continue;

            double label = CardMetrics.LabelMinWidth;
            foreach (Metric m in rows)
            {
                foreach (string word in m.Label.Split(' '))
                    Assert.True(Width(word, CardMetrics.LabelFontSize, FontWeights.Normal, scale) <= CardMetrics.LabelMaxWidth,
                        $"{part.Kind}: label word '{word}' is wider than the label column");
                label = Math.Max(label, Math.Min(CardMetrics.LabelMaxWidth, Width(m.Label, CardMetrics.LabelFontSize, FontWeights.Normal, scale)));
            }

            double value = CardMetrics.ValueMinWidth;
            foreach (Metric m in rows)
            foreach (FormattedValue v in Values(m, t))
                value = Math.Max(value, Width(v.Value, CardMetrics.ValueFontSize, FontWeights.SemiBold, scale)
                                        + CardMetrics.UnitGap + Width(v.Unit, CardMetrics.UnitFontSize, FontWeights.Normal, scale));

            // Label, value and the gaps are Auto columns; min/max gets the rest of the row.
            double minMaxColumn = content - label - value - 2 * CardMetrics.ColumnGap;
            foreach (Metric m in rows)
            foreach (string half in MinMaxHalves(m, t))
            {
                double w = Width(half, CardMetrics.MinMaxFontSize, FontWeights.Normal, scale);
                Assert.True(w <= minMaxColumn - rounding,
                    $"{part.Kind} at {scale} (compact {compact}, {t}): '{half}' needs {w:0.0} px, min/max column has {minMaxColumn:0.0} px " +
                    $"(label {label:0.0}, value {value:0.0}, content {content:0.0})");
            }
        }
    });

    [Theory]
    [MemberData(nameof(Cases))]
    public void HeroTextFitsItsColumn(double scale, bool compact) => Sta.Run(() =>
    {
        double rounding = 1 / scale;
        double hero = CardMetrics.HeroWidth(CardMetrics.ContentWidth(CardMetrics.MinColumnWidth, compact));
        foreach (TemperatureUnit t in Enum.GetValues<TemperatureUnit>())
        foreach (PartSnapshot part in Parts())
        {
            CardLayout layout = CardLayout.For(part.Kind);
            foreach (string? key in new[] { layout.HeroKey, layout.SecondHeroKey })
            {
                if (key is null || part.Find(key) is not Metric m)
                    continue;
                foreach (FormattedValue v in Values(m, t))
                {
                    double w = Width(v.Value, CardMetrics.HeroValueFontSize, FontWeights.SemiBold, scale)
                               + CardMetrics.HeroUnitGap + Width(v.Unit, CardMetrics.HeroUnitFontSize, FontWeights.Normal, scale);
                    Assert.True(w <= hero - rounding,
                        $"{part.Kind} hero '{v}' at {scale} (compact {compact}) needs {w:0.0} px, column has {hero:0.0} px");
                }
                foreach (string half in MinMaxHalves(m, t))
                {
                    double w = Width(half, CardMetrics.HeroMinMaxFontSize, FontWeights.Normal, scale);
                    Assert.True(w <= hero - rounding,
                        $"{part.Kind} hero min/max '{half}' at {scale} (compact {compact}) needs {w:0.0} px, column has {hero:0.0} px");
                }
            }
        }
    });
}
