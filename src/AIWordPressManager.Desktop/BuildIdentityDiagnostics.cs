using Serilog;

namespace AIWordPressManager.Desktop;

internal static class BuildIdentityDiagnostics
{
    private static int _logged;

    public static string Summary =>
        $"Version={BuildIdentityDisplay.Version} | Branch={BuildIdentityDisplay.Branch} | Commit={BuildIdentityDisplay.Commit}";

    public static void LogOnce()
    {
        if (Interlocked.Exchange(ref _logged, 1) != 0)
            return;

        Log.Information(
            "Application build identity: Version={Version}, Branch={Branch}, Commit={Commit}",
            BuildIdentityDisplay.Version,
            BuildIdentityDisplay.Branch,
            BuildIdentityDisplay.Commit);
    }
}
