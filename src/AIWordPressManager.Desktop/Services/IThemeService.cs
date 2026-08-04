namespace AIWordPressManager.Desktop.Services;

public interface IThemeService
{
    bool IsDarkTheme { get; }
    string CurrentPalette { get; }
    string CurrentFontPalette { get; }
    void ApplyDarkTheme();
    void ApplyLightTheme();
    void ToggleTheme();
    void ApplyAccentPalette(string paletteName);
    void ApplyFontPalette(string paletteName);
}
