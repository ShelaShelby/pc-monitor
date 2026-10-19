using System.Windows.Media;
using PcMonitor.Core;

namespace PcMonitor.App.ViewModels;

/// <summary>Brushes shared by view-models (frozen, so they are cheap and thread-safe).</summary>
public static class Palette
{
    public static readonly Brush Text = Make(0xE6, 0xE9, 0xEF);
    public static readonly Brush Dim = Make(0x8A, 0x93, 0xA6);
    public static readonly Brush Accent = Make(0x4E, 0xA1, 0xFF);
    public static readonly Brush Accent2 = Make(0xB3, 0x8B, 0xFF);
    public static readonly Brush Ok = Make(0x3D, 0xDC, 0x84);
    public static readonly Brush Warn = Make(0xFF, 0xC8, 0x57);
    public static readonly Brush Critical = Make(0xFF, 0x5C, 0x5C);
    public static readonly Brush Unknown = Make(0x8A, 0x93, 0xA6);

    public static Brush For(ThresholdLevel level) => level switch
    {
        ThresholdLevel.Ok => Ok,
        ThresholdLevel.Warn => Warn,
        ThresholdLevel.Critical => Critical,
        _ => Unknown,
    };

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
