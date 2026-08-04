namespace AIWordPressManager.Desktop;

public sealed record StartupProgress(int Percentage, string Stage, string Detail)
{
    public static StartupProgress Create(int percentage, string stage, string detail) =>
        new(Math.Clamp(percentage, 0, 100), stage, detail);
}
