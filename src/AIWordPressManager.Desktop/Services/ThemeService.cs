using System.IO;
using System.Windows.Media;

namespace AIWordPressManager.Desktop.Services;

public sealed class ThemeService : IThemeService
{
    private readonly string _preferenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "theme-palette.txt");

    private readonly string _fontPreferenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "font-palette.txt");

    private readonly string _modeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "theme-mode.txt");

    public bool IsDarkTheme { get; private set; } = false;
    public string CurrentPalette { get; private set; } = "Brand Teal";
    public string CurrentFontPalette { get; private set; } = "Adaptive Contrast";

    public ThemeService()
    {
        try
        {
            if (File.Exists(_preferenceFile)) CurrentPalette = File.ReadAllText(_preferenceFile).Trim();
            if (File.Exists(_fontPreferenceFile)) CurrentFontPalette = File.ReadAllText(_fontPreferenceFile).Trim();
            if (File.Exists(_modeFile)) IsDarkTheme = !string.Equals(File.ReadAllText(_modeFile).Trim(), "light", StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        ApplyAccentPalette(CurrentPalette, persist: false);
        ApplyFontPalette(CurrentFontPalette, persist: false);
    }

    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplyAccentPalette(CurrentPalette, persist: true);
        ApplyFontPalette(CurrentFontPalette, persist: false);
    }

    public void ApplyDarkTheme()
    {
        IsDarkTheme = true;
        ApplyAccentPalette(CurrentPalette, persist: true);
        ApplyFontPalette(CurrentFontPalette, persist: false);
    }

    public void ApplyLightTheme()
    {
        IsDarkTheme = false;
        ApplyAccentPalette(CurrentPalette, persist: true);
        ApplyFontPalette(CurrentFontPalette, persist: false);
    }

    public void ApplyAccentPalette(string paletteName) => ApplyAccentPalette(paletteName, persist: true);

    public void ApplyFontPalette(string paletteName) => ApplyFontPalette(paletteName, persist: true);

    private void ApplyFontPalette(string paletteName, bool persist)
    {
        var normalized = NormalizeFontPaletteName(paletteName);
        CurrentFontPalette = normalized;

        var background = IsDarkTheme ? Parse("#0B0F17") : Parse("#F4F7FB");
        var surface = IsDarkTheme ? Parse("#151C29") : Parse("#FFFFFF");
        var headerSurface = IsDarkTheme ? Parse("#16243A") : Parse("#E2E8F0");
        var sidebar = IsDarkTheme ? Parse("#111827") : Parse("#FFFFFF");

        var colors = normalized switch
        {
            "Cool Slate" => (Primary: Parse(IsDarkTheme ? "#E8EEF7" : "#172033"), Secondary: Parse(IsDarkTheme ? "#A9B8CC" : "#526174"), Muted: Parse(IsDarkTheme ? "#718096" : "#7B8797")),
            "Warm Graphite" => (Primary: Parse(IsDarkTheme ? "#F5F1EA" : "#292521"), Secondary: Parse(IsDarkTheme ? "#C5BDB2" : "#625B52"), Muted: Parse(IsDarkTheme ? "#81796F" : "#817A72")),
            "Azure Ink" => (Primary: Parse(IsDarkTheme ? "#EAF2FF" : "#102A56"), Secondary: Parse(IsDarkTheme ? "#ACC4EA" : "#395A87"), Muted: Parse(IsDarkTheme ? "#718DB4" : "#7185A0")),
            "Emerald Ink" => (Primary: Parse(IsDarkTheme ? "#E8FFF6" : "#123D31"), Secondary: Parse(IsDarkTheme ? "#A6D9C5" : "#3F6E5D"), Muted: Parse(IsDarkTheme ? "#6D9F8D" : "#718D82")),
            "Violet Ink" => (Primary: Parse(IsDarkTheme ? "#F4EEFF" : "#33205C"), Secondary: Parse(IsDarkTheme ? "#C8B6EB" : "#65518A"), Muted: Parse(IsDarkTheme ? "#8977AE" : "#82749A")),
            "High Contrast" => (Primary: BestReadableText(background), Secondary: BestReadableText(surface), Muted: EnsureContrast(Mix(BestReadableText(surface), surface, 0.32), surface, 4.5)),
            _ => (Primary: BestReadableText(background), Secondary: EnsureContrast(Mix(BestReadableText(surface), surface, 0.30), surface, 4.5), Muted: EnsureContrast(Mix(BestReadableText(surface), surface, 0.48), surface, 3.2))
        };

        Set("TextPrimaryBrush", EnsureContrast(colors.Primary, background, 7.0));
        Set("TextSecondaryBrush", EnsureContrast(colors.Secondary, surface, 4.5));
        Set("TextDisabledBrush", EnsureContrast(colors.Muted, surface, 3.0));
        Set("HeaderTextBrush", EnsureContrast(colors.Primary, headerSurface, 7.0));
        Set("SidebarTextBrush", EnsureContrast(colors.Primary, sidebar, 7.0));
        Set("SidebarMutedTextBrush", EnsureContrast(colors.Secondary, sidebar, 4.5));
        Set("SidebarGroupTextBrush", EnsureContrast(colors.Primary, sidebar, 7.0));
        Set("SidebarDisabledTextBrush", EnsureContrast(colors.Muted, sidebar, 3.0));

        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fontPreferenceFile)!);
            File.WriteAllText(_fontPreferenceFile, CurrentFontPalette);
        }
        catch { }
    }

    private void ApplyAccentPalette(string paletteName, bool persist)
    {
        var normalized = NormalizePaletteName(paletteName);
        var palette = normalized switch
        {
            "Brand Teal" => (Primary: "#16B6A6", Secondary: "#22D3B0"),
            "Emerald Slate" => (Primary: "#10B981", Secondary: "#22C55E"),
            "Violet Pulse" => (Primary: "#8B5CF6", Secondary: "#D946EF"),
            "Ocean Cyan" => (Primary: "#0891B2", Secondary: "#22D3EE"),
            "Crimson Ember" => (Primary: "#E11D48", Secondary: "#F97316"),
            "Graphite Gold" => (Primary: "#D4A72C", Secondary: "#F59E0B"),
            "Rose Quartz" => (Primary: "#DB2777", Secondary: "#A855F7"),
            _ => (Primary: "#16B6A6", Secondary: "#22D3B0")
        };

        CurrentPalette = normalized;
        var primary = Parse(palette.Primary);
        var secondary = Parse(palette.Secondary);

        var background = IsDarkTheme ? Parse("#071528") : Parse("#F8FAFC");
        var sidebar = Parse("#0F172A");
        var surface = IsDarkTheme ? Parse("#151C29") : Parse("#FFFFFF");
        var surfaceAlt = IsDarkTheme ? Parse("#16243A") : Parse("#F1F5F9");
        var controlSurface = IsDarkTheme ? Parse("#0F1724") : Parse("#F8FAFD");
        var headerSurface = IsDarkTheme ? Parse("#16243A") : Parse("#E2E8F0");
        var headerText = IsDarkTheme ? Parse("#F8FAFC") : Parse("#172033");
        var textPrimary = IsDarkTheme ? Parse("#F8FAFC") : Parse("#0F172A");
        var textSecondary = IsDarkTheme ? Parse("#CBD5E1") : Parse("#64748B");
        var textDisabled = IsDarkTheme ? Parse("#667085") : Parse("#98A2B3");
        var border = IsDarkTheme ? Parse("#2A3648") : Parse("#D7E0EA");
        var borderStrong = IsDarkTheme ? Parse("#3A4960") : Parse("#B9C6D6");

        var accent = primary;
        var accentHover = Mix(primary, secondary, 0.24);
        var accentPressed = Mix(primary, Colors.Black, IsDarkTheme ? 0.18 : 0.10);
        var selection = WithAlpha(Mix(primary, secondary, 0.35), IsDarkTheme ? (byte)0x58 : (byte)0x28);
        var accentSoft = WithAlpha(Mix(primary, secondary, 0.45), IsDarkTheme ? (byte)0x2B : (byte)0x1A);

        Set("PrimaryBrush", accent);
        Set("PrimaryHoverBrush", accentHover);
        Set("PrimaryPressedBrush", accentPressed);
        Set("SecondaryBrush", secondary);
        Set("SecondaryHoverBrush", Mix(secondary, primary, 0.18));
        Set("AppBackgroundBrush", background);
        Set("SidebarBrush", sidebar);
        Set("SurfaceBrush", surface);
        Set("SurfaceAltBrush", surfaceAlt);
        Set("ControlSurfaceBrush", controlSurface);
        Set("HeaderSurfaceBrush", headerSurface);
        Set("HeaderTextBrush", headerText);
        Set("TextPrimaryBrush", textPrimary);
        Set("TextSecondaryBrush", textSecondary);
        Set("TextDisabledBrush", textDisabled);
        Set("BorderBrush", border);
        Set("BorderStrongBrush", borderStrong);
        Set("SelectionBrush", selection);
        Set("AccentSoftBrush", accentSoft);
        Set("AccentGlowBrush", WithAlpha(secondary, 0x48));
        Set("OnAccentBrush", BestReadableText(accent));
        Set("OnSecondaryBrush", BestReadableText(secondary));
        Set("OverlayBrush", WithAlpha(Colors.Black, IsDarkTheme ? (byte)0xA8 : (byte)0x78));
        Set("SuccessBrush", Parse("#22C55E"));
        Set("WarningBrush", Parse("#F59E0B"));
        Set("DangerBrush", Parse("#EF4444"));
        Set("InfoBrush", Parse("#38BDF8"));
        Set("GoldOnLightBrush", IsDarkTheme ? accentHover : Mix(accent, Colors.Black, 0.18));

        var sidebarText = BestReadableText(sidebar);
        var sidebarMuted = EnsureContrast(Mix(sidebarText, sidebar, 0.34), sidebar, 4.5);
        var sidebarGroup = EnsureContrast(Mix(sidebarText, accent, 0.16), sidebar, 4.5);
        var sidebarDisabled = EnsureContrast(Mix(sidebarText, sidebar, 0.55), sidebar, 3.0);
        var sidebarHover = IsLight(sidebar) ? Mix(sidebar, Colors.Black, 0.07) : Mix(sidebar, Colors.White, 0.08);
        var sidebarSearch = IsLight(sidebar) ? Mix(sidebar, Colors.Black, 0.035) : Mix(sidebar, Colors.White, 0.045);
        var sidebarBorder = IsLight(sidebar) ? Mix(sidebar, Colors.Black, 0.16) : Mix(sidebar, Colors.White, 0.18);

        Set("SidebarTextBrush", sidebarText);
        Set("SidebarMutedTextBrush", sidebarMuted);
        Set("SidebarGroupTextBrush", sidebarGroup);
        Set("SidebarHoverBrush", sidebarHover);
        Set("SidebarSelectedTextBrush", BestReadableText(accent));
        Set("SidebarDisabledTextBrush", sidebarDisabled);
        Set("SidebarSearchSurfaceBrush", sidebarSearch);
        Set("SidebarBorderBrush", sidebarBorder);

        Set("CoolingBackgroundBrush", IsDarkTheme ? Parse("#102638") : Parse("#E8F7FF"));
        Set("CoolingBorderBrush", Parse("#38BDF8"));
        Set("CoolingTextBrush", IsDarkTheme ? Parse("#E0F2FE") : Parse("#075985"));
        ApplyFontPalette(CurrentFontPalette, persist: false);

        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferenceFile)!);
            File.WriteAllText(_preferenceFile, CurrentPalette);
            File.WriteAllText(_modeFile, IsDarkTheme ? "dark" : "light");
        }
        catch { }
    }

    private static string NormalizePaletteName(string? paletteName) => paletteName?.Trim() switch
    {
        "Brand" or "Brand Teal" or "AI WordPress" => "Brand Teal",
        "Sapphire" or "Sapphire Blue" or "Midnight Azure" => "Midnight Azure",
        "Emerald" or "Emerald Green" or "Emerald Slate" => "Emerald Slate",
        "Amethyst" or "Amethyst Purple" or "Violet Pulse" => "Violet Pulse",
        "Cyan" or "Ocean Cyan" => "Ocean Cyan",
        "Coral" or "Coral Red" or "Crimson Ember" => "Crimson Ember",
        "Royal Gold" or "Graphite Gold" => "Graphite Gold",
        "Rose" or "Rose Quartz" => "Rose Quartz",
        _ => "Brand Teal"
    };

    private static string NormalizeFontPaletteName(string? paletteName) => paletteName?.Trim() switch
    {
        "Cool Slate" => "Cool Slate",
        "Warm Graphite" => "Warm Graphite",
        "Azure Ink" => "Azure Ink",
        "Emerald Ink" => "Emerald Ink",
        "Violet Ink" => "Violet Ink",
        "High Contrast" => "High Contrast",
        _ => "Adaptive Contrast"
    };

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static Color Mix(Color a, Color b, double amountOfB)
    {
        amountOfB = Math.Clamp(amountOfB, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(a.R + ((b.R - a.R) * amountOfB)),
            (byte)Math.Round(a.G + ((b.G - a.G) * amountOfB)),
            (byte)Math.Round(a.B + ((b.B - a.B) * amountOfB)));
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    private static bool IsLight(Color color) => RelativeLuminance(color) >= 0.50;

    private static Color BestReadableText(Color background)
    {
        var dark = Parse("#0B1220");
        var light = Parse("#FFFFFF");
        return ContrastRatio(dark, background) >= ContrastRatio(light, background) ? dark : light;
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Color EnsureContrast(Color preferred, Color background, double minimumRatio)
    {
        if (ContrastRatio(preferred, background) >= minimumRatio) return preferred;
        return BestReadableText(background);
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void Set(string key, Color color)
    {
        var application = System.Windows.Application.Current;
        if (application is null) return;
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        application.Resources[key] = brush;
    }
}
