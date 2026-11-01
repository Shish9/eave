using System;
using System.Windows;
using System.Windows.Media;

namespace ClaudeIsland;

/// Colour sets for the island. The window reads them through dynamic resources, so changing
/// a theme repaints everything immediately.
public class ThemeColors
{
    public Color Bg, Fg, Fg2, Fg3, Btn, BtnFg, Track, Outline, Accent;

    public bool IsLight => 0.299 * Bg.R + 0.587 * Bg.G + 0.114 * Bg.B > 150;

    public static readonly string[] Presets = { "Dark", "Midnight", "Graphite", "Clay", "Light", "Custom" };

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color Hex(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); } catch { return fallback; }
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static ThemeColors For(AppSettings s) => s.Theme switch
    {
        "Midnight" => new ThemeColors
        {
            Bg = C("#0A1022"), Fg = C("#EAF0FF"), Fg2 = C("#B5C2E6"), Fg3 = C("#7C8AB0"),
            Btn = C("#1B2547"), BtnFg = C("#EAF0FF"), Track = C("#1D2850"), Outline = C("#334A68B0"), Accent = C("#5B8CFF"),
        },
        "Graphite" => new ThemeColors
        {
            Bg = C("#1C1C1E"), Fg = C("#F2F2F7"), Fg2 = C("#C7C7CC"), Fg3 = C("#8E8E93"),
            Btn = C("#3A3A3C"), BtnFg = C("#F2F2F7"), Track = C("#3A3A3C"), Outline = C("#33FFFFFF"), Accent = C("#0A84FF"),
        },
        "Clay" => new ThemeColors
        {
            Bg = C("#1A1210"), Fg = C("#F7EDE6"), Fg2 = C("#D8C4B8"), Fg3 = C("#A08E82"),
            Btn = C("#3A2A24"), BtnFg = C("#F7EDE6"), Track = C("#3A2A24"), Outline = C("#33FFE0CC"), Accent = C("#D97757"),
        },
        "Light" => new ThemeColors
        {
            Bg = C("#F4F4F7"), Fg = C("#111113"), Fg2 = C("#3C3C43"), Fg3 = C("#6B6B72"),
            Btn = C("#E1E1E8"), BtnFg = C("#111113"), Track = C("#D5D5DD"), Outline = C("#22000000"), Accent = C("#2E6BFF"),
        },
        "Custom" => Custom(s),
        _ => new ThemeColors
        {
            Bg = C("#000000"), Fg = C("#FFFFFF"), Fg2 = C("#C8C8C8"), Fg3 = C("#8C8C8C"),
            Btn = C("#2B2B2B"), BtnFg = C("#FFFFFF"), Track = C("#2A2A2A"), Outline = C("#33FFFFFF"), Accent = C("#2E86FF"),
        },
    };

    static ThemeColors Custom(AppSettings s)
    {
        var bg = Hex(s.CustomBg, C("#0B0B0F"));
        var fg = Hex(s.CustomText, Colors.White);
        return new ThemeColors
        {
            Bg = bg, Fg = fg,
            Fg2 = Mix(bg, fg, 0.78), Fg3 = Mix(bg, fg, 0.52),
            Btn = Mix(bg, fg, 0.14), BtnFg = fg, Track = Mix(bg, fg, 0.16),
            Outline = Color.FromArgb(0x33, fg.R, fg.G, fg.B),
            Accent = Hex(s.CustomAccent, C("#2E86FF")),
        };
    }

    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public void ApplyTo(ResourceDictionary r)
    {
        r["BgBrush"] = B(Bg);
        r["FgBrush"] = B(Fg);
        r["Fg2Brush"] = B(Fg2);
        r["Fg3Brush"] = B(Fg3);
        r["BtnBrush"] = B(Btn);
        r["BtnFgBrush"] = B(BtnFg);
        r["TrackBrush"] = B(Track);
        r["OutlineBrush"] = B(Outline);
        r["AccentBrush"] = B(Accent);
    }
}
